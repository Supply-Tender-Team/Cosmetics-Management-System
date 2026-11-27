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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        var tenders = await _db.Tenders.AsNoTracking()
            .Where(t => t.CommitteeMembers.Any(m => m.UserId == userId)
                && t.Bids.Any(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted)
                && t.PublishDate != null && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation
                || t.Status == Statuses.TenderStatus.AwaitingManagerReview || t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision))
            .OrderByDescending(t => t.CreatedAt)
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



        // Sourcing sends this notification after compliance review.
        var sharedLinks = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Type == "TenderEvaluation" && n.Link != null)
            .Select(n => n.Link).ToListAsync();
        tenders = tenders.Where(t => sharedLinks.Contains(Url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();

        var model = new EvaluationDashboardViewModel();
        model.AssignedTenderCount = tenders.Count;
        model.UnderEvaluationCount = tenders.Count(t => t.Status == Statuses.TenderStatus.Published
            || t.Status == Statuses.TenderStatus.Open || t.Status == Statuses.TenderStatus.Closed
            || t.Status == Statuses.TenderStatus.UnderEvaluation);
        model.AwaitingReviewCount = tenders.Count(t =>
            t.Status == Statuses.TenderStatus.AwaitingManagerReview
            || t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision);
        model.Search = search?.Trim();
        var tenderIds = tenders.Where(t => t.Status != Statuses.TenderStatus.AwaitingManagerReview
            && t.Status != Statuses.TenderStatus.AwaitingCommitteeDecision).Select(t => t.Id).ToList();
        model.EligibleOfferCount = await _db.Bids.AsNoTracking().CountAsync(b =>
            tenderIds.Contains(b.TenderId)
            && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted));

        foreach (var tender in tenders)
        {
            if (string.IsNullOrEmpty(model.Search)
                || tender.TenderNumber.Contains(model.Search, StringComparison.OrdinalIgnoreCase)
                || tender.Title.Contains(model.Search, StringComparison.OrdinalIgnoreCase))
            {
                model.Tenders.Add(tender);
            }
        }

        return View(model);
    }
}
