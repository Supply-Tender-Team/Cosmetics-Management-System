using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Areas.Evaluation.Services;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class EvaluationController : Controller
{
    private readonly ApplicationDbContext _db;

    public EvaluationController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        var query = _db.Tenders.AsNoTracking().Where(t =>
            t.Bids.Any(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted)
            && t.PublishDate != null && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation)
            && t.CommitteeMembers.Any(m => m.UserId == userId));

        var model = new EvaluationDashboardViewModel();

        model.Search = search?.Trim();
        if (!string.IsNullOrEmpty(model.Search))
        {
            query = query.Where(t => t.TenderNumber.Contains(model.Search) || t.Title.Contains(model.Search));
        }

        model.Tenders = await query.OrderBy(t => t.SubmissionDeadline)
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
        model.Tenders = model.Tenders.Where(t => sharedLinks.Contains(Url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();

        model.AssignedTenderCount = model.Tenders.Count;
        return View(model);
    }

    public async Task<IActionResult> Offers(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        var tender = await _db.Tenders.AsNoTracking().Where(t => t.Id == id
            && t.PublishDate != null && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation)
            && t.CommitteeMembers.Any(m => m.UserId == userId))
            .Select(t => new EvaluationOffersViewModel
            {
                TenderId = t.Id,
                TenderNumber = t.TenderNumber,
                Title = t.Title,
                Description = t.Description
            }).SingleOrDefaultAsync();

        if (tender == null) return NotFound();
        var sharedLink = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id });
        if (!await _db.Notifications.AnyAsync(n => n.UserId == userId && n.Type == "TenderEvaluation"
            && n.Link == sharedLink)) return NotFound();

        tender.Offers = await _db.Bids.AsNoTracking().Where(b => b.TenderId == id
            && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
            .OrderBy(b => b.SupplierProfile.CompanyName)
            .Select(b => new EvaluationOfferViewModel
            {
                Id = b.Id,
                CompanyName = b.SupplierProfile.CompanyName,
                TotalPrice = b.TotalPrice,
                Currency = b.Currency,
                DeliveryDays = b.DeliveryDays,
                ValidityDays = b.ValidityDays,
                PaymentTerms = b.PaymentTerms,
                TechnicalSummary = b.TechnicalSummary
            }).ToListAsync();

        tender.Recommendation = await new CommitteeRecommendationService(_db).ReadAsync(id, userId, sharedLink!);
        return View(tender);
    }
}
