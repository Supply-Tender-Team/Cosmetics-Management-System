using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class ComplianceController : Controller
{
    private static readonly string[] ReviewableBidStatuses =
    [
        Statuses.BidStatus.Submitted,
        Statuses.BidStatus.UnderReview,
        Statuses.BidStatus.Compliant,
        Statuses.BidStatus.NonCompliant
    ];

    private static readonly string[] ComplianceResults =
    [
        Statuses.ComplianceResult.Compliant,
        Statuses.ComplianceResult.NonCompliant,
        Statuses.ComplianceResult.PendingClarification
    ];

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;

    public ComplianceController(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? tenderId, int? bidId, CancellationToken cancellationToken)
    {
        var model = await BuildPageAsync(tenderId, bidId, null, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [Bind(Prefix = "Decision")] ComplianceDecisionInputViewModel input,
        CancellationToken cancellationToken)
    {
        if (!ComplianceResults.Contains(input.Result, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Decision.Result", "Choose one of the available compliance decisions.");
        }

        var bidIsReviewable = await IsReviewableBidAsync(input.TenderId, input.BidId, cancellationToken);
        if (!bidIsReviewable) return NotFound();

        if (!ModelState.IsValid)
        {
            var page = await BuildPageAsync(input.TenderId, input.BidId, input, cancellationToken);
            return page is null ? NotFound() : View(nameof(Index), page);
        }

        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(reviewerId)) return Challenge();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var bidStatus = input.Result == Statuses.ComplianceResult.PendingClarification
            ? Statuses.BidStatus.UnderReview : input.Result;
        var changed = await _db.Bids.Where(bid => bid.Id == input.BidId && bid.TenderId == input.TenderId
                && bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status))
            .ExecuteUpdateAsync(update => update.SetProperty(bid => bid.Status, bidStatus), cancellationToken);
        if (changed == 0) return NotFound();

        var check = new ComplianceCheck
        {
            BidId = input.BidId,
            CheckedByUserId = reviewerId,
            Result = input.Result,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
            CheckedAt = DateTime.UtcNow
        };

        _db.ComplianceChecks.Add(check);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Checked",
            "ComplianceCheck",
            check.Id.ToString(),
            $"{check.Result} review saved for Bid {check.BidId}.");

        await transaction.CommitAsync(cancellationToken);

        TempData["ToastTitle"] = "Compliance decision saved";
        TempData["ToastMessage"] = "The review was saved successfully.";
        TempData["ToastType"] = "success";

        // Return to the tender overview after saving so the decision form closes.
        return RedirectToAction(nameof(Index), new { tenderId = input.TenderId });
    }

    private async Task<CompliancePageViewModel?> BuildPageAsync(
        int? tenderId,
        int? bidId,
        ComplianceDecisionInputViewModel? decision,
        CancellationToken cancellationToken)
    {
        var tenders = await _db.Tenders.AsNoTracking()
            .Where(tender => tender.Bids.Any(bid =>
                bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status)))
            .OrderByDescending(tender => tender.CreatedAt)
            .ThenByDescending(tender => tender.Id)
            .Select(tender => new CompliancePageViewModel.TenderOption(
                tender.Id, tender.TenderNumber, tender.Title, tender.Status, tender.CreatedAt, tender.SubmissionDeadline))
            .ToListAsync(cancellationToken);

        if (tenderId.HasValue && tenders.All(tender => tender.Id != tenderId.Value)) return null;
        if (bidId.HasValue && !tenderId.HasValue) return null;

        CompliancePageViewModel.TenderDetails? selectedTender = null;
        List<CompliancePageViewModel.BidRow> bids = [];
        CompliancePageViewModel.BidDetails? selectedBid = null;
        List<CompliancePageViewModel.ExtractedRequirement> requirements = [];

        if (tenderId.HasValue)
        {
            selectedTender = await _db.Tenders.AsNoTracking()
                .Where(tender => tender.Id == tenderId.Value
                    && tender.Bids.Any(bid => bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status)))
                .Select(tender => new CompliancePageViewModel.TenderDetails(
                    tender.Id, tender.TenderNumber, tender.Title, tender.Description, tender.Terms,
                    tender.Status, tender.SubmissionDeadline))
                .SingleOrDefaultAsync(cancellationToken);

            if (selectedTender is null) return null;

            requirements = ComplianceTermsExtractor.Extract(selectedTender.Terms);

            bids = await _db.Bids.AsNoTracking()
                .Where(bid => bid.TenderId == tenderId.Value
                    && bid.SubmittedAt.HasValue
                    && ReviewableBidStatuses.Contains(bid.Status))
                .OrderByDescending(bid => bid.SubmittedAt)
                .ThenByDescending(bid => bid.Id)
                .Select(bid => new CompliancePageViewModel.BidRow(
                    bid.Id,
                    bid.SupplierProfile.CompanyName,
                    bid.SubmittedAt!.Value,
                    bid.TotalPrice,
                    bid.Currency,
                    bid.Status,
                    bid.ComplianceChecks
                        .OrderByDescending(check => check.CheckedAt)
                        .ThenByDescending(check => check.Id)
                        .Select(check => check.Result)
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);

            if (bidId.HasValue && bids.All(bid => bid.Id != bidId.Value)) return null;

            if (bidId.HasValue)
            {
                selectedBid = await _db.Bids.AsNoTracking()
                    .Where(bid => bid.Id == bidId.Value
                        && bid.TenderId == tenderId.Value
                        && bid.SubmittedAt.HasValue
                        && ReviewableBidStatuses.Contains(bid.Status))
                    .Select(bid => new CompliancePageViewModel.BidDetails(
                        bid.Id,
                        bid.TenderId,
                        bid.SupplierProfile.CompanyName,
                        bid.SupplierProfile.Category,
                        bid.SupplierProfile.Country,
                        bid.Status,
                        bid.TotalPrice,
                        bid.Currency,
                        bid.DeliveryDays,
                        bid.ValidityDays,
                        bid.PaymentTerms,
                        bid.TechnicalSummary,
                        bid.SubmittedAt!.Value))
                    .SingleOrDefaultAsync(cancellationToken);

                if (selectedBid is null) return null;

                var documents = await _db.BidDocuments.AsNoTracking()
                    .Where(document => document.BidId == bidId.Value)
                    .OrderByDescending(document => document.UploadedAt)
                    .ThenByDescending(document => document.Id)
                    .Select(document => new CompliancePageViewModel.DocumentRow(
                        document.Id,
                        document.DocType,
                        document.FileName,
                        document.UploadedAt,
                        document.SizeBytes))
                    .ToListAsync(cancellationToken);

                requirements = requirements.Select(requirement => requirement with
                {
                    RelatedDocuments = documents
                        .Where(document => ComplianceTermsExtractor.CouldRelateDocument(
                            requirement.Name, document.DocType, document.FileName))
                        .ToList()
                }).ToList();

                var reviews = await _db.ComplianceChecks.AsNoTracking()
                    .Where(check => check.BidId == bidId.Value)
                    .OrderByDescending(check => check.CheckedAt)
                    .ThenByDescending(check => check.Id)
                    .Select(check => new CompliancePageViewModel.ReviewRow(
                        check.Result,
                        check.CheckedByUser.FullName,
                        check.CheckedAt,
                        check.Reason))
                    .ToListAsync(cancellationToken);

                selectedBid = selectedBid with { Documents = documents, Reviews = reviews };
            }
        }

        return new CompliancePageViewModel
        {
            Tenders = tenders,
            Requirements = requirements,
            SelectedTenderId = tenderId,
            SelectedTender = selectedTender,
            Bids = bids,
            SelectedBid = selectedBid,
            Decision = decision ?? new ComplianceDecisionInputViewModel
            {
                TenderId = tenderId ?? 0,
                BidId = bidId ?? 0
            }
        };
    }

    private Task<bool> IsReviewableBidAsync(int tenderId, int bidId, CancellationToken cancellationToken) =>
        _db.Bids.AsNoTracking().AnyAsync(bid =>
            bid.Id == bidId
            && bid.TenderId == tenderId
            && bid.SubmittedAt.HasValue
            && ReviewableBidStatuses.Contains(bid.Status), cancellationToken);
}
