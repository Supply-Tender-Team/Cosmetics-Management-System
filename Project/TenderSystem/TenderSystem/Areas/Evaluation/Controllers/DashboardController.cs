using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class DashboardController : Controller
{
    // OWNER: Marwa

    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? search)
    {
        // Identify the signed-in member before querying assigned tenders.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        // Read only published, assigned tenders with at least one eligible offer.
        var assignedTenders = await _db.Tenders.AsNoTracking()
            .Where(t => t.CommitteeMembers.Any(m => m.UserId == userId)
                && t.Bids.Any(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted)
                && t.PublishDate != null && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation
                || t.Status == Statuses.TenderStatus.AwaitingManagerReview || t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision))
            .OrderByDescending(t => t.CreatedAt)
            // Copy only the tender fields needed by the dashboard table.
            .Select(t => new EvaluationTenderViewModel
            {
                Id = t.Id,
                TenderNumber = t.TenderNumber,
                Title = t.Title,
                Status = t.Status,
                SubmissionDeadline = t.SubmissionDeadline,
                MemberRole = t.CommitteeMembers.Where(m => m.UserId == userId)
                    .Select(m => m.MemberRole).FirstOrDefault() ?? string.Empty
            }).ToListAsync();


        // A saved notification must point to this tender's committee brief.
        var committeeBriefLinks = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Type == "TenderEvaluation" && n.Link != null)
            .Select(n => n.Link).ToListAsync();
        assignedTenders = assignedTenders.Where(t => committeeBriefLinks.Contains(Url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();

        // Dashboard cards: count the full authorized list before applying text search.
        var model = new EvaluationDashboardViewModel();
        // Assigned tenders = every tender remaining after membership and shared-brief checks.
        model.AssignedTenderCount = assignedTenders.Count;
        // Under evaluation = assigned tenders in Published, Open, Closed or Under Evaluation.
        model.UnderEvaluationCount = assignedTenders.Count(t => t.Status == Statuses.TenderStatus.Published
            || t.Status == Statuses.TenderStatus.Open || t.Status == Statuses.TenderStatus.Closed
            || t.Status == Statuses.TenderStatus.UnderEvaluation);
        // Awaiting decisions = assigned tenders waiting for Manager or Top Committee.
        model.AwaitingReviewCount = assignedTenders.Count(t =>
            t.Status == Statuses.TenderStatus.AwaitingManagerReview
            || t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision);
        model.Search = search?.Trim();
        // Waiting tenders remain visible, but are not counted as active evaluation offers.
        var activeTenderIds = assignedTenders.Where(t => t.Status != Statuses.TenderStatus.AwaitingManagerReview
            && t.Status != Statuses.TenderStatus.AwaitingCommitteeDecision).Select(t => t.Id).ToList();
        // Eligible offers = Compliant/Accepted bids in active assigned tenders, not all site bids.
        model.EligibleOfferCount = await _db.Bids.AsNoTracking().CountAsync(b =>
            activeTenderIds.Contains(b.TenderId)
            && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted));

        // Search filters the table; the summary cards retain the full assigned scope.
        model.Tenders = assignedTenders.Where(t => string.IsNullOrEmpty(model.Search)
            || t.TenderNumber.Contains(model.Search, StringComparison.OrdinalIgnoreCase)
            || t.Title.Contains(model.Search, StringComparison.OrdinalIgnoreCase)).ToList();

        // Send the counters and filtered rows to Views/Dashboard/Index.cshtml.
        return View(model);
    }
}
