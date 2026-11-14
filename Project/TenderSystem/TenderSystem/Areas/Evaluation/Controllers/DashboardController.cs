using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee)]
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
                && t.Status != Statuses.TenderStatus.Draft)
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

        var model = new EvaluationDashboardViewModel();
        model.AssignedTenderCount = tenders.Count;
        model.UnderEvaluationCount = tenders.Count(t => t.Status == Statuses.TenderStatus.UnderEvaluation);
        model.AwaitingReviewCount = tenders.Count(t =>
            t.Status == Statuses.TenderStatus.AwaitingManagerReview
            || t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision);
        model.Search = search?.Trim();
        model.EligibleOfferCount = await _db.Bids.AsNoTracking().CountAsync(b =>
            b.Tender.Status == Statuses.TenderStatus.UnderEvaluation
            && b.Tender.CommitteeMembers.Any(m => m.UserId == userId)
            && b.Status == Statuses.BidStatus.Compliant
            && b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                .Select(c => c.Result).FirstOrDefault() == Statuses.ComplianceResult.Compliant);

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
