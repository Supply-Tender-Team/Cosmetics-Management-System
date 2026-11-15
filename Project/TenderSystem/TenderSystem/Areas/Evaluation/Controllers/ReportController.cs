using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;
using System.Data;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class ReportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly NotificationService _notify;
    private readonly AiBidAnalysisService _ai;

    public ReportController(ApplicationDbContext db, AuditService audit, NotificationService notify, AiBidAnalysisService ai)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
        _ai = ai;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var model = await BuildPageAsync(id, userId, ct);
        return model == null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([Bind(Prefix = "Input")] SubmitReportViewModel input, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var model = await BuildPageAsync(input.TenderId, userId, ct);
        if (model == null) return NotFound();
        if (!model.CanSubmit) ModelState.AddModelError("", model.PendingMessage ?? "This report has already been submitted.");
        if (!model.Offers.Any(o => o.Eligible && o.BidId == input.RecommendedBidId))
            ModelState.AddModelError("Input.RecommendedBidId", "Choose an eligible offer from this tender.");
        input.Justification = input.Justification?.Trim() ?? string.Empty;
        if (input.Justification.Length < 20 || input.Justification.Length > 4000)
            ModelState.AddModelError("Input.Justification", "Explain the committee recommendation in 20 to 4,000 characters.");
        if (!ModelState.IsValid)
        {
            model.Input = input;
            return View("Index", model);
        }
        var report = await _db.EvaluationReports.SingleOrDefaultAsync(r => r.TenderId == input.TenderId, ct);
        if (report != null) return Conflict("A report already exists. Review it before submitting again.");
        var technical = new StringBuilder("Committee technical results (80 points).\n");
        var financial = new StringBuilder("Distinct-price financial ranking (20 points).\n");
        foreach (var offer in model.Offers)
        {
            technical.AppendLine($"Offer #{offer.BidId} — {offer.Supplier}: {offer.Status}.");
            if (!offer.Eligible) continue;
            foreach (var member in offer.MemberPoints) technical.AppendLine($"{member.Key}: {member.Value:0.##}/80");
            technical.AppendLine($"Committee average: {offer.TechnicalPoints:0.##}/80");
            foreach (var note in offer.Notes) technical.AppendLine(note);
            financial.AppendLine($"Offer #{offer.BidId} — {offer.Supplier}: {offer.Price:0.00} {offer.Currency}; financial {offer.FinancialPoints:0.##}/20; total {offer.TotalPoints:0.##}/100.");
        }
        report = new EvaluationReport
        {
            TenderId = input.TenderId, Status = Statuses.ReportStatus.RecommendationPending,
            RecommendedBidId = input.RecommendedBidId, TechnicalSummary = technical.ToString(),
            FinancialSummary = financial.ToString(), Justification = input.Justification,
            PreparedByUserId = userId, CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow
        };
        _db.EvaluationReports.Add(report);
        await _db.SaveChangesAsync(ct);
        var officerId = await _db.Tenders.Where(t => t.Id == input.TenderId).Select(t => t.CreatedByUserId).SingleAsync(ct);
        var link = Url.Action(nameof(Index), "Report", new { area = "Evaluation", id = input.TenderId })
            ?? throw new InvalidOperationException("Could not generate report link.");
        await _notify.NotifyAsync(officerId, "EvaluationReportReady", "Committee report ready",
            $"The committee report for {model.TenderNumber} is ready for your review and forwarding.", link);
        await _audit.LogAsync("SubmittedToOfficer", "EvaluationReport", report.Id.ToString(), model.TenderNumber);
        // The Officer must forward it; do not move the tender to AwaitingManagerReview here.
        await transaction.CommitAsync(ct);
        TempData["ReportSaved"] = "Report sent to the Sourcing Officer. It has not been forwarded to the Manager.";
        return RedirectToAction(nameof(Index), new { id = input.TenderId });
    }

    private async Task<UnifiedReportViewModel?> BuildPageAsync(int? id, string userId, CancellationToken ct)
    {
        var isOfficer = User.IsInRole(Roles.SourcingOfficer);
        var tenders = await _db.Tenders.AsNoTracking()
            .Where(t => t.PublishDate != null && t.Status != Statuses.TenderStatus.Draft
                && (t.CommitteeMembers.Any(m => m.UserId == userId) || (isOfficer && t.CreatedByUserId == userId)))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new EvaluationTenderViewModel { Id = t.Id, TenderNumber = t.TenderNumber, Title = t.Title })
            .ToListAsync(ct);
        var links = await _db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Type == "TenderEvaluation")
            .Select(n => n.Link).ToListAsync(ct);
        var officerTenderIds = isOfficer ? await _db.EvaluationReports.Where(r => r.Tender.CreatedByUserId == userId)
            .Select(r => r.TenderId).ToListAsync(ct) : new List<int>();
        tenders = tenders.Where(t => officerTenderIds.Contains(t.Id) || links.Contains(Url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();
        var model = new UnifiedReportViewModel { Tenders = tenders, TenderId = id };
        if (!id.HasValue) return model;
        var selected = tenders.FirstOrDefault(t => t.Id == id);
        if (selected == null) return null;
        model.TenderNumber = selected.TenderNumber;
        model.Input.TenderId = id.Value;
        var tender = await _db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.CommitteeMembers).ThenInclude(m => m.User)
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations)
            .SingleAsync(t => t.Id == id, ct);
        var criteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).OrderBy(c => c.Id).ToList();
        var members = tender.CommitteeMembers.OrderBy(m => m.Id).ToList();
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        var eligible = tender.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted).ToList();
        var validRubric = criteria.Count == 6 && criteria.Sum(c => c.WeightPercent) == 80
            && tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent) == 20
            && tender.EvaluationCriteria.All(c => c.IsLocked && c.MaxScore > 0 && c.WeightPercent > 0)
            && members.Count == 4 && members.Select(m => m.UserId).Distinct().Count() == 4
            && seats.All(seat => members.Count(m => m.MemberRole == seat) == 1)
            && tender.EvaluationCriteria.Count(c => c.Type == Statuses.CriterionType.Financial) == 1;
        var comparable = eligible.Count > 0 && eligible.All(b => b.TotalPrice > 0 && !string.IsNullOrWhiteSpace(b.Currency))
            && eligible.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && !tender.Bids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview);
        var prices = eligible.Select(b => b.TotalPrice).Distinct().OrderBy(p => p).ToList();
        foreach (var bid in tender.Bids.Where(b => b.Status != Statuses.BidStatus.Draft).OrderBy(b => b.TotalPrice))
        {
            var row = new ReportOfferViewModel { BidId = bid.Id, Supplier = bid.SupplierProfile.CompanyName,
                Status = bid.Status, Eligible = eligible.Any(b => b.Id == bid.Id), Price = bid.TotalPrice, Currency = bid.Currency };
            if (row.Eligible && validRubric)
            {
                foreach (var member in members)
                {
                    var scores = new List<EvaluationScoreViewModel>();
                    foreach (var criterion in criteria)
                    {
                        var saved = bid.TechnicalEvaluations.Where(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.Id)
                            .OrderByDescending(e => e.EvaluatedAt).ThenByDescending(e => e.Id).FirstOrDefault();
                        scores.Add(new EvaluationScoreViewModel { Score = saved?.Score, MaxScore = criterion.MaxScore, WeightPercent = criterion.WeightPercent });
                        if (!string.IsNullOrWhiteSpace(saved?.Notes)) row.Notes.Add($"{member.User.FullName} — {criterion.Name}: {saved.Notes}");
                    }
                    row.MemberPoints.Add(member.MemberRole + " — " + member.User.FullName, AssessmentsController.CalculateTechnicalPoints(scores));
                    var financialNote = bid.FinancialEvaluations.Where(e => e.EvaluatorUserId == member.UserId)
                        .OrderByDescending(e => e.EvaluatedAt).FirstOrDefault()?.Notes;
                    if (!string.IsNullOrWhiteSpace(financialNote)) row.Notes.Add($"{member.User.FullName} — financial: {financialNote}");
                }
                if (row.MemberPoints.Values.All(p => p.HasValue)) row.TechnicalPoints = row.MemberPoints.Values.Average();
                if (comparable) row.FinancialPoints = AssessmentsController.CalculateFinancialPoints(prices, bid.TotalPrice, 20);
            }
            model.Offers.Add(row);
        }
        var existing = await _db.EvaluationReports.AsNoTracking().FirstOrDefaultAsync(r => r.TenderId == id, ct);
        model.SubmittedAt = existing?.SubmittedAt;
        model.TechnicalSnapshot = existing?.TechnicalSummary ?? string.Empty;
        model.FinancialSnapshot = existing?.FinancialSummary ?? string.Empty;
        model.SubmittedJustification = existing?.Justification ?? string.Empty;
        model.SubmittedBidId = existing?.RecommendedBidId;
        var activeStage = tender.Status == Statuses.TenderStatus.Published || tender.Status == Statuses.TenderStatus.Open
            || tender.Status == Statuses.TenderStatus.Closed || tender.Status == Statuses.TenderStatus.UnderEvaluation;
        model.CanSubmit = existing == null && activeStage && members.Any(m => m.UserId == userId)
            && validRubric && comparable && model.Offers.Where(o => o.Eligible).All(o => o.TechnicalPoints.HasValue);
        if (!model.CanSubmit && existing == null)
            model.PendingMessage = "Pending: submission needs a dispatched tender in evaluation, four assigned members, the locked 80/20 rubric, complete scores from all four members for every eligible offer, and comparable prices after compliance review.";
        if (existing != null && existing.SubmittedAt == null) model.PendingMessage = "An existing report needs review before another can be submitted.";
        if (members.Any(m => m.UserId == userId)) model.AiItems = await _ai.GetLatestAsync(id.Value, userId, ct);
        return model;
    }
}
