using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class CommitteeBriefController(ApplicationDbContext db, IFileStorageService files) : Controller
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
        return stream is null ? NotFound() : File(stream, "application/octet-stream", document.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEvaluation(int id, int bidId, Dictionary<int, decimal> scores, Dictionary<int, string?> notes)
    {
        var evaluatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var bid = await db.Bids.Include(b => b.Tender).ThenInclude(t => t.EvaluationCriteria)
            .Include(b => b.TechnicalEvaluations.Where(score => score.EvaluatorUserId == evaluatorId))
            .FirstOrDefaultAsync(b => b.Id == bidId && b.TenderId == id && b.Tender.PublishDate != null
                && b.Tender.Status != "Draft" && b.Tender.CommitteeMembers.Any(m => m.UserId == evaluatorId)
                && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted));
        if (bid is null || evaluatorId is null) return NotFound();

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
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["EvaluationSuccess"] = "Your technical evaluation was saved.";
        return RedirectToAction(nameof(Index), new { id });
    }
}