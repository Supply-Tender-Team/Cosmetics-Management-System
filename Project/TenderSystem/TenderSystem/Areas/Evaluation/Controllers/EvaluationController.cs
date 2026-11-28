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

    // Evaluation list: load only tenders the current member can evaluate.
    public async Task<IActionResult> Index(string? search)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        // Role access alone is not enough: the user must be assigned to this tender.
        var assignedTendersQuery = _db.Tenders.AsNoTracking().Where(t =>
            t.Bids.Any(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted)
            && t.PublishDate != null && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation)
            && t.CommitteeMembers.Any(m => m.UserId == userId));

        // Trim the optional search text; add its condition before executing the SQL query.
        var model = new EvaluationDashboardViewModel { Search = search?.Trim() };
        if (!string.IsNullOrEmpty(model.Search))
        {
            assignedTendersQuery = assignedTendersQuery.Where(t => t.TenderNumber.Contains(model.Search) || t.Title.Contains(model.Search));
        }

        // Run the filtered query, order by deadline, and project each result into a display row.
        model.Tenders = await assignedTendersQuery.OrderBy(t => t.SubmissionDeadline)
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
        model.Tenders = model.Tenders.Where(t => committeeBriefLinks.Contains(Url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();

        // This page counts the final visible rows, including its search and shared-brief filters.
        model.AssignedTenderCount = model.Tenders.Count;
        // Return the prepared ViewModel to the view named after this action.
        return View(model);
    }

    [HttpGet]
    // Tender brief: load one authorized tender with department, requested items and criteria.
    public async Task<IActionResult> Brief(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        // Include follows relationships: Tender -> Requisition -> Department/Items, plus Criteria.
        var tender = await _db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.Requisition).ThenInclude(r => r.Department)
            .Include(t => t.Requisition).ThenInclude(r => r.PurchaseRequisitionItems)
            .Include(t => t.EvaluationCriteria)
            .SingleOrDefaultAsync(t => t.Id == id && t.PublishDate != null && t.Status != Statuses.TenderStatus.Draft
                && t.CommitteeMembers.Any(m => m.UserId == userId));
        if (tender == null) return NotFound();
        // Build the expected Sourcing brief URL; require a matching notification for this member.
        var committeeBriefLink = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id });
        if (!await _db.Notifications.AnyAsync(n => n.UserId == userId && n.Type == "TenderEvaluation" && n.Link == committeeBriefLink)) return NotFound();
        return View(tender);
    }

    // Offers page: id is TenderId; each Evaluate link later sends a BidId instead.
    public async Task<IActionResult> Offers(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        // Project only the fields this page needs into its ViewModel.
        var model = await _db.Tenders.AsNoTracking().Where(t => t.Id == id
            && t.PublishDate != null && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation)
            && t.CommitteeMembers.Any(m => m.UserId == userId))
            .Select(t => new EvaluationOffersViewModel
            {
                TenderId = t.Id,
                TenderNumber = t.TenderNumber,
                Title = t.Title,
                Description = t.Description
            }).SingleOrDefaultAsync();

        if (model == null) return NotFound();
        // Build the expected Sourcing brief URL; require a matching notification for this member.
        var committeeBriefLink = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id });
        if (!await _db.Notifications.AnyAsync(n => n.UserId == userId && n.Type == "TenderEvaluation"
            && n.Link == committeeBriefLink)) return NotFound();

        // Compliant and Accepted are the only offer statuses shown for evaluation.
        model.Offers = await _db.Bids.AsNoTracking().Where(b => b.TenderId == id
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

        // Read saved progress to label each offer Evaluate, Edit or View; this does not cast a vote.
        model.Recommendation = await new CommitteeRecommendationService(_db).ReadAsync(id, userId, committeeBriefLink!);
        foreach (var offer in model.Offers)
        {
            offer.HasSavedEvaluation = model.Recommendation?.Offers.FirstOrDefault(o => o.BidId == offer.Id)?.HasSavedEvaluation == true;
            offer.IsReady = model.Recommendation?.IsReady == true || model.Recommendation?.IsSubmitted == true;
        }
        // Return the prepared ViewModel to the view named after this action.
        return View(model);
    }
}
