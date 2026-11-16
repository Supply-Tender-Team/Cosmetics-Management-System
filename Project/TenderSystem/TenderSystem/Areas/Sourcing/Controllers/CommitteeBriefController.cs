using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class CommitteeBriefController(ApplicationDbContext db, IFileStorageService files, AuditService audit) : Controller
{
    public async Task<IActionResult> Index(int id)
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Requisition).ThenInclude(request => request.Department)
            .Include(t => t.Requisition).ThenInclude(request => request.PurchaseRequisitionItems)
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
                .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
                .ThenInclude(b => b.BidItems).ThenInclude(item => item.RequisitionItem)
            .Include(t => t.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
                .ThenInclude(b => b.TechnicalEvaluations.Where(score => score.EvaluatorUserId == memberId))
            .Include(t => t.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
                .ThenInclude(b => b.BidDocuments)
            .FirstOrDefaultAsync(t => t.Id == id && t.PublishDate != null && t.Status != "Draft"
                && t.CommitteeMembers.Any(member => member.UserId == memberId));
        return tender is null ? NotFound() : View(tender);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadOfferDocument(int id, CancellationToken cancellationToken)
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var document = await db.BidDocuments.AsNoTracking()
            .FirstOrDefaultAsync(document => document.Id == id && document.Bid.Tender.PublishDate != null
                && document.Bid.Tender.Status != "Draft"
                && document.Bid.Tender.CommitteeMembers.Any(member => member.UserId == memberId)
                && (document.Bid.Status == Statuses.BidStatus.Compliant || document.Bid.Status == Statuses.BidStatus.Accepted),
                cancellationToken);
        if (document is null) return NotFound();

        var stream = await files.OpenReadAsync(document.FilePath, cancellationToken);
        if (stream is null) return NotFound();

        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        var contentType = extension == ".pdf" ? "application/pdf" : "application/octet-stream";
        if (contentType == "application/pdf")
        {
            if (string.IsNullOrWhiteSpace(memberId))
            {
                await stream.DisposeAsync();
                return Challenge();
            }

            var downloadedAt = DateTime.UtcNow;
            var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(
                db, memberId, "BidDocument", id, Statuses.DocumentAction.Download, downloadedAt, cancellationToken);
            await using (stream)
            {
                var viewerName = history.FirstOrDefault()?.ViewerName ?? "Signed-in user";
                var webRootPath = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
                var watermarkedContent = await DocumentWatermarkService.CreateWatermarkedCopyAsync(
                    stream, document.FileName, viewerName, downloadedAt, webRootPath, cancellationToken, history);
                Response.Headers.CacheControl = "no-store";
                return File(watermarkedContent, contentType, document.FileName);
            }
        }

        return File(stream, contentType, document.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEvaluation(int id, int bidId, Dictionary<int, decimal> scores, Dictionary<int, string?> notes)
    {
        var evaluatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (evaluatorId is null) return NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Tenders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}");
        var bid = await db.Bids.Include(b => b.Tender).ThenInclude(t => t.EvaluationCriteria)
            .Include(b => b.TechnicalEvaluations.Where(score => score.EvaluatorUserId == evaluatorId))
            .FirstOrDefaultAsync(b => b.Id == bidId && b.TenderId == id && b.Tender.PublishDate != null
                && b.Tender.Status != "Draft" && b.Tender.CommitteeMembers.Any(m => m.UserId == evaluatorId)
                && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted));
        if (bid is null) return NotFound();

        var financial = await db.FinancialEvaluations.SingleOrDefaultAsync(e =>
            e.BidId == bid.Id && e.EvaluatorUserId == evaluatorId);
        if (financial?.ReadyAt is not null || await db.EvaluationCommitteeSubmissions.AnyAsync(e =>
                e.TenderId == id && e.EvaluatorUserId == evaluatorId))
        {
            TempData["EvaluationError"] = "Your evaluation is already Ready and cannot be edited.";
            return RedirectToAction(nameof(Index), new { id });
        }

        if (await db.EvaluationReports.AnyAsync(report => report.TenderId == id && report.SubmittedAt != null))
        {
            TempData["EvaluationError"] = "The evaluation report has already been submitted and scores cannot be edited.";
            return RedirectToAction(nameof(Index), new { id });
        }

        var criteria = bid.Tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).ToArray();
        if (!ModelState.IsValid || criteria.Length == 0 || scores.Count != criteria.Length ||
            criteria.Any(c => !scores.TryGetValue(c.Id, out var score) || score < 0 || score > c.MaxScore ||
                decimal.Round(score, 2) != score) || notes.Values.Any(note => note?.Length > 1000))
        {
            TempData["EvaluationError"] = "Enter a score in the displayed range for every question. Notes can have up to 1,000 characters.";
            return RedirectToAction(nameof(Index), new { id });
        }

        var now = DateTime.UtcNow;
        foreach (var criterion in criteria)
        {
            var evaluation = bid.TechnicalEvaluations.Where(score => score.CriterionId == criterion.Id)
                .OrderByDescending(score => score.EvaluatedAt).ThenByDescending(score => score.Id).FirstOrDefault();
            if (evaluation is null)
            {
                evaluation = new TechnicalEvaluation { BidId = bid.Id, CriterionId = criterion.Id, EvaluatorUserId = evaluatorId };
                bid.TechnicalEvaluations.Add(evaluation);
            }
            evaluation.Score = scores[criterion.Id];
            evaluation.Notes = notes.GetValueOrDefault(criterion.Id)?.Trim();
            evaluation.EvaluatedAt = now;
        }
        if (financial is not null)
        {
            financial.CompletedAt = null;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync("Evaluated", "Bid", bid.Id.ToString(), "Technical evaluation saved from committee brief.");
        await transaction.CommitAsync();
        TempData["EvaluationSuccess"] = "Your technical evaluation was saved.";
        return RedirectToAction(nameof(Index), new { id });
    }
}
