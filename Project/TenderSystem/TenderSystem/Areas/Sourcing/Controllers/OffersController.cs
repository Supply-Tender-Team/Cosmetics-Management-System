using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Email;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class OffersController : Controller
{
    private const int MinimumOffersBeforeResend = 3;
    private const int MaximumResends = 3;
    private const string ResendAuditAction = "Resend";
    private const string ResendAuditEntity = "Tender";

    private static readonly string[] EligibleBidStatuses =
    [
        Statuses.BidStatus.Submitted,
        Statuses.BidStatus.UnderReview,
        Statuses.BidStatus.Compliant,
        Statuses.BidStatus.NonCompliant
    ];

    private static readonly string[] ValidComplianceResults =
    [
        Statuses.ComplianceResult.Compliant,
        Statuses.ComplianceResult.NonCompliant,
        Statuses.ComplianceResult.PendingClarification
    ];

    private static readonly string[] ValidSortFields = ["supplier", "price", "submitted", "delivery", "validity", "compliance"];

    private static readonly string[] SubmittedBidStatuses =
    [
        Statuses.BidStatus.Submitted,
        Statuses.BidStatus.UnderReview,
        Statuses.BidStatus.Compliant,
        Statuses.BidStatus.NonCompliant,
        Statuses.BidStatus.Accepted,
        Statuses.BidStatus.Rejected
    ];

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;
    private readonly EmailService _email;
    private readonly ILogger<OffersController> _logger;

    public OffersController(
        ApplicationDbContext db,
        AuditService audit,
        NotificationService notifications,
        EmailService email,
        ILogger<OffersController> logger)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _email = email;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Compare(
        int? tenderId,
        CompareOffersFilters? filters,
        CancellationToken cancellationToken)
    {
        filters ??= new CompareOffersFilters();
        filters.SupplierName = filters.SupplierName?.Trim();
        filters.Currency = filters.Currency?.Trim();

        var now = DateTime.UtcNow;
        var tenderQuery = _db.Tenders.AsNoTracking()
            .Where(tender => tender.Id == tenderId
                || tender.Bids.Any(bid => bid.SubmittedAt.HasValue
                    && EligibleBidStatuses.Contains(bid.Status))
                || ((tender.Status == Statuses.TenderStatus.Published
                        || tender.Status == Statuses.TenderStatus.Open
                        || (tender.Status == Statuses.TenderStatus.Closed && tender.SubmissionDeadline <= now))
                    && tender.TenderSuppliers.Any(supplier => supplier.ParticipationStatus != Statuses.ParticipationStatus.Withdrawn)));

        var tenders = await tenderQuery
            .OrderByDescending(tender => tender.CreatedAt)
            .ThenByDescending(tender => tender.Id)
            .Select(tender => new CompareOffersViewModel.TenderOption(
                tender.Id,
                tender.TenderNumber,
                tender.Title,
                tender.Status,
                tender.CreatedAt,
                tender.SubmissionDeadline,
                tender.Bids.Where(bid => bid.SubmittedAt.HasValue && SubmittedBidStatuses.Contains(bid.Status))
                    .Select(bid => bid.SupplierProfileId).Distinct().Count()))
            .ToListAsync(cancellationToken);

        var model = new CompareOffersViewModel
        {
            Tenders = tenders,
            SelectedTenderId = tenderId,
            Filters = filters,
            FilterErrors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "One of the filter values is invalid." : error.ErrorMessage)
                .Distinct()
                .ToList()
        };

        if (!tenderId.HasValue)
        {
            return View(model with
            {
                PageMessage = tenders.Count == 0 ? "No tenders currently have eligible submitted offers." : null
            });
        }

        var selectedTender = await _db.Tenders.AsNoTracking()
            .Where(tender => tender.Id == tenderId.Value)
            .Select(tender => new CompareOffersViewModel.TenderDetails(
                tender.Id, tender.TenderNumber, tender.Title, tender.Status, tender.SubmissionDeadline,
                tender.Bids.Where(bid => bid.SubmittedAt.HasValue && SubmittedBidStatuses.Contains(bid.Status))
                    .Select(bid => bid.SupplierProfileId).Distinct().Count()))
            .SingleOrDefaultAsync(cancellationToken);

        if (selectedTender is null)
        {
            return View(model with { PageMessage = "The selected tender could not be found. Choose an available tender below." });
        }

        var resendsUsed = await CountResendsAsync(tenderId.Value, cancellationToken);
        var resendableSuppliers = await GetResendableSuppliersAsync(tenderId.Value, cancellationToken);
        var resendMessage = GetResendMessage(
            selectedTender.Status,
            selectedTender.SubmissionDeadline,
            selectedTender.SubmittedSupplierCount,
            resendsUsed,
            resendableSuppliers.Count,
            await HasEvaluationStartedAsync(tenderId.Value, cancellationToken));
        selectedTender = selectedTender with
        {
            ResendsUsed = resendsUsed,
            ResendableSupplierCount = resendableSuppliers.Count,
            CanResend = string.IsNullOrEmpty(resendMessage),
            ResendMessage = resendMessage
        };
        model = model with
        {
            SelectedTender = selectedTender,
            SubmittedSupplierCount = selectedTender.SubmittedSupplierCount,
            ResendsUsed = resendsUsed,
            MaxResends = MaximumResends,
            ResendableSupplierCount = resendableSuppliers.Count,
            CanResend = selectedTender.CanResend,
            ResendMessage = resendMessage
        };

        var eligibleBids = _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tenderId.Value
                && bid.SubmittedAt.HasValue
                && EligibleBidStatuses.Contains(bid.Status));

        var eligibleCount = await eligibleBids.CountAsync(cancellationToken);
        if (eligibleCount == 0)
        {
            return View(model with { PageMessage = "This tender has no submitted offers to compare yet." });
        }

        var rawCurrencies = await eligibleBids
            .Select(bid => bid.Currency)
            .Distinct()
            .OrderBy(currency => currency)
            .ToListAsync(cancellationToken);

        var currencySummaryRows = await eligibleBids
            .GroupBy(bid => bid.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                OfferCount = group.Count(),
                LowestPrice = group.Min(bid => bid.TotalPrice),
                HighestPrice = group.Max(bid => bid.TotalPrice),
                AveragePrice = group.Average(bid => bid.TotalPrice)
            })
            .OrderBy(summary => summary.Currency)
            .ToListAsync(cancellationToken);

        var currencies = rawCurrencies.Select(currency => string.IsNullOrWhiteSpace(currency) ? "__missing__" : currency).ToList();
        var suppliers = await eligibleBids
            .Where(bid => bid.SupplierProfile.CompanyName != "")
            .Select(bid => bid.SupplierProfile.CompanyName)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
        var availablePrices = await eligibleBids.Select(bid => bid.TotalPrice).Distinct().OrderBy(price => price).ToListAsync(cancellationToken);
        var availableDeliveryDays = await eligibleBids.Where(bid => bid.DeliveryDays > 0).Select(bid => bid.DeliveryDays).Distinct().OrderBy(days => days).ToListAsync(cancellationToken);
        var availableValidityDays = await eligibleBids.Where(bid => bid.ValidityDays > 0).Select(bid => bid.ValidityDays).Distinct().OrderBy(days => days).ToListAsync(cancellationToken);
        var availableSubmissionDates = await eligibleBids.Where(bid => bid.SubmittedAt.HasValue).Select(bid => bid.SubmittedAt!.Value.Date).Distinct().OrderBy(date => date).ToListAsync(cancellationToken);
        var currencySummaries = currencySummaryRows.Select(summary => new CompareOffersViewModel.CurrencySummary(
            string.IsNullOrWhiteSpace(summary.Currency) ? "Not Provided" : summary.Currency,
            summary.OfferCount,
            summary.LowestPrice,
            summary.HighestPrice,
            summary.AveragePrice)).ToList();

        var supplierCount = await eligibleBids.Select(bid => bid.SupplierProfileId).Distinct().CountAsync(cancellationToken);
        var earliestDelivery = await eligibleBids.Where(bid => bid.DeliveryDays > 0)
            .Select(bid => (int?)bid.DeliveryDays).MinAsync(cancellationToken);
        var latestDelivery = await eligibleBids.Where(bid => bid.DeliveryDays > 0)
            .Select(bid => (int?)bid.DeliveryDays).MaxAsync(cancellationToken);

        ValidateFilters(filters, currencies, suppliers, currencySummaries.Count);
        model = model with
        {
            FilterErrors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "One of the filter values is invalid." : error.ErrorMessage)
                .Distinct()
                .ToList()
        };

        var matchingBids = eligibleBids;
        if (!string.IsNullOrWhiteSpace(filters.SupplierName) && suppliers.Contains(filters.SupplierName, StringComparer.Ordinal))
            matchingBids = matchingBids.Where(bid => bid.SupplierProfile.CompanyName == filters.SupplierName);

        if (EligibleBidStatuses.Contains(filters.BidStatus ?? string.Empty, StringComparer.Ordinal))
            matchingBids = matchingBids.Where(bid => bid.Status == filters.BidStatus);

        if (filters.ComplianceResult == "__none__")
            matchingBids = matchingBids.Where(bid => !bid.ComplianceChecks.Any());
        else if (ValidComplianceResults.Contains(filters.ComplianceResult ?? string.Empty, StringComparer.Ordinal))
            matchingBids = matchingBids.Where(bid => bid.ComplianceChecks
                .OrderByDescending(check => check.CheckedAt)
                .ThenByDescending(check => check.Id)
                .Select(check => check.Result)
                .FirstOrDefault() == filters.ComplianceResult);

        if (!string.IsNullOrWhiteSpace(filters.Currency) && currencies.Contains(filters.Currency, StringComparer.Ordinal))
            matchingBids = filters.Currency == "__missing__"
                ? matchingBids.Where(bid => bid.Currency == "")
                : matchingBids.Where(bid => bid.Currency == filters.Currency);

        var mayFilterPrice = currencySummaries.Count <= 1 || currencies.Contains(filters.Currency ?? string.Empty, StringComparer.Ordinal);
        var priceRangeIsValid = (!filters.MinimumPrice.HasValue || filters.MinimumPrice.Value >= 0)
            && (!filters.MaximumPrice.HasValue || filters.MaximumPrice.Value >= 0)
            && (!filters.MinimumPrice.HasValue || !filters.MaximumPrice.HasValue
                || filters.MinimumPrice.Value <= filters.MaximumPrice.Value);
        if (mayFilterPrice && priceRangeIsValid && filters.MinimumPrice.HasValue)
            matchingBids = matchingBids.Where(bid => bid.TotalPrice >= filters.MinimumPrice.Value);
        if (mayFilterPrice && priceRangeIsValid && filters.MaximumPrice.HasValue)
            matchingBids = matchingBids.Where(bid => bid.TotalPrice <= filters.MaximumPrice.Value);

        if (filters.MaximumDeliveryDays is > 0)
            matchingBids = matchingBids.Where(bid => bid.DeliveryDays > 0 && bid.DeliveryDays <= filters.MaximumDeliveryDays.Value);
        if (filters.MinimumValidityDays is > 0)
            matchingBids = matchingBids.Where(bid => bid.ValidityDays >= filters.MinimumValidityDays.Value);
        if (filters.HasDocuments.HasValue)
            matchingBids = filters.HasDocuments.Value
                ? matchingBids.Where(bid => bid.BidDocuments.Any())
                : matchingBids.Where(bid => !bid.BidDocuments.Any());
        var submittedDateRangeIsValid = !filters.SubmittedFrom.HasValue || !filters.SubmittedTo.HasValue
            || filters.SubmittedFrom.Value.Date <= filters.SubmittedTo.Value.Date;
        if (submittedDateRangeIsValid && filters.SubmittedFrom.HasValue)
            matchingBids = matchingBids.Where(bid => bid.SubmittedAt >= filters.SubmittedFrom.Value.Date);
        if (submittedDateRangeIsValid && filters.SubmittedTo.HasValue)
        {
            var dayAfterSubmittedTo = filters.SubmittedTo.Value.Date.AddDays(1);
            matchingBids = matchingBids.Where(bid => bid.SubmittedAt < dayAfterSubmittedTo);
        }

        matchingBids = ApplySort(matchingBids, filters.SortBy, filters.Descending, currencySummaries.Count > 1);
        var matchingCount = await matchingBids.CountAsync(cancellationToken);

        var offerRows = await matchingBids
            .Select(bid => new CompareOffersViewModel.OfferRow(
                bid.Id,
                bid.SupplierProfile.CompanyName,
                bid.SupplierProfile.TaxNumber,
                bid.SubmittedAt!.Value,
                bid.TotalPrice,
                bid.Currency,
                bid.DeliveryDays,
                bid.ValidityDays,
                bid.PaymentTerms,
                bid.TechnicalSummary,
                bid.Status,
                bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                    .ThenByDescending(check => check.Id)
                    .Select(check => check.Result)
                    .FirstOrDefault(),
                bid.BidDocuments.Count,
                new List<CompareOffersViewModel.BidItemRow>(),
                new List<CompareOffersViewModel.DocumentRow>()))
            .ToListAsync(cancellationToken);

        var bidIds = offerRows.Select(row => row.Id).ToList();
        if (bidIds.Count > 0)
        {
            var items = await _db.BidItems.AsNoTracking()
                .Where(item => bidIds.Contains(item.BidId))
                .OrderBy(item => item.Id)
                .Select(item => new
                {
                    item.BidId,
                    Row = new CompareOffersViewModel.BidItemRow(
                        item.RequisitionItem.ItemName,
                        item.RequisitionItem.Unit,
                        item.Quantity,
                        item.UnitPrice,
                        item.TaxAmount,
                        item.ShippingCost,
                        item.InstallationCost,
                        item.OfferedSpecifications)
                })
                .ToListAsync(cancellationToken);

            var documents = await _db.BidDocuments.AsNoTracking()
                .Where(document => bidIds.Contains(document.BidId))
                .OrderBy(document => document.Id)
                .Select(document => new
                {
                    document.BidId,
                    Row = new CompareOffersViewModel.DocumentRow(
                        document.Id, document.DocType, document.FileName, document.UploadedAt, document.SizeBytes)
                })
                .ToListAsync(cancellationToken);

            offerRows = offerRows.Select(row => row with
            {
                Items = items.Where(item => item.BidId == row.Id).Select(item => item.Row).ToList(),
                Documents = documents.Where(document => document.BidId == row.Id).Select(document => document.Row).ToList()
            }).ToList();
        }

        return View(model with
        {
            EligibleOfferCount = eligibleCount,
            MatchingOfferCount = matchingCount,
            SupplierCount = supplierCount,
            EarliestDeliveryDays = earliestDelivery,
            LatestDeliveryDays = latestDelivery,
            CurrencySummaries = currencySummaries,
            AvailableCurrencies = currencies,
            AvailableSuppliers = suppliers,
            AvailablePrices = availablePrices,
            AvailableDeliveryDays = availableDeliveryDays,
            AvailableValidityDays = availableValidityDays,
            AvailableSubmissionDates = availableSubmissionDates,
            Offers = offerRows
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resend(int tenderId, DateTime shownDeadline, int shownResendsUsed, CancellationToken cancellationToken)
    {
        List<ResendSupplier> recipients;
        DateTime currentDeadline;

        await using (var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            var tender = await _db.Tenders.SingleOrDefaultAsync(row => row.Id == tenderId, cancellationToken);
            if (tender is null) return NotFound();
            if (tender.SubmissionDeadline != shownDeadline)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["ResendError"] = "This tender was already updated. Reload the offers page and review its current status.";
                return RedirectToAction(nameof(Compare), new { tenderId });
            }

            var resendsUsed = await CountResendsAsync(tenderId, cancellationToken);
            if (resendsUsed != shownResendsUsed)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["ResendError"] = "Another resend was just processed. Reload the offers page to see the updated attempt count.";
                return RedirectToAction(nameof(Compare), new { tenderId });
            }

            var submittedSupplierCount = await CountSubmittedSuppliersAsync(tenderId, cancellationToken);
            var evaluationStarted = await HasEvaluationStartedAsync(tenderId, cancellationToken);
            recipients = await GetResendableSuppliersAsync(tenderId, cancellationToken);
            var blockedReason = GetResendMessage(
                tender.Status, tender.SubmissionDeadline, submittedSupplierCount,
                resendsUsed, recipients.Count, evaluationStarted);
            if (!string.IsNullOrEmpty(blockedReason))
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["ResendError"] = blockedReason;
                return RedirectToAction(nameof(Compare), new { tenderId });
            }

            currentDeadline = tender.SubmissionDeadline;

            var supplierIds = recipients.Select(recipient => recipient.SupplierProfileId).ToList();
            var previousInvitations = await _db.Invitations
                .Where(invitation => invitation.TenderId == tenderId && supplierIds.Contains(invitation.SupplierProfileId))
                .OrderByDescending(invitation => invitation.SentAt)
                .ThenByDescending(invitation => invitation.Id)
                .ToListAsync(cancellationToken);
            var latestInvitations = previousInvitations
                .GroupBy(invitation => invitation.SupplierProfileId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var recipient in recipients)
            {
                if (!latestInvitations.TryGetValue(recipient.SupplierProfileId, out var invitation))
                {
                    _db.Invitations.Add(new Invitation
                    {
                        TenderId = tenderId,
                        SupplierProfileId = recipient.SupplierProfileId,
                        Status = Statuses.InvitationStatus.Pending,
                        SentAt = DateTime.UtcNow,
                        ExpiresAt = currentDeadline
                    });
                    continue;
                }

                invitation.Status = Statuses.InvitationStatus.Pending;
                invitation.SentAt = DateTime.UtcNow;
                invitation.ExpiresAt = currentDeadline;
                invitation.RespondedAt = null;
                invitation.DeclineReason = null;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync(
                ResendAuditAction,
                ResendAuditEntity,
                tenderId.ToString(),
                $"Resend {resendsUsed + 1} of {MaximumResends}; existing deadline remains {currentDeadline:u}; {recipients.Count} selected supplier(s) reinvited.",
                $"SubmissionDeadline={tender.SubmissionDeadline:u}; ResendsUsed={resendsUsed}");
            await transaction.CommitAsync(cancellationToken);
        }

        var subject = $"Tender invitation renewed: {tenderId}";
        var message = $"A tender you were selected for has been sent again. Please submit your offer by {currentDeadline:u}.";
        var invitationsUrl = _email.AbsoluteUrl("/Supplier/Invitations");
        foreach (var recipient in recipients)
        {
            try
            {
                await _notifications.NotifyAsync(
                    recipient.UserId,
                    "TenderInvitation",
                    "Tender invitation renewed",
                    message,
                    "/Supplier/Invitations");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not create resend notification for supplier profile {SupplierProfileId}.", recipient.SupplierProfileId);
            }

            if (!await _email.SendAsync(recipient.Email, subject,
                    EmailTemplates.Notification("Tender invitation renewed", message, invitationsUrl), cancellationToken))
            {
                _logger.LogWarning("Tender resend email could not be sent for supplier profile {SupplierProfileId}.", recipient.SupplierProfileId);
            }
        }

        TempData["ToastTitle"] = "Tender resent";
        TempData["ToastMessage"] = $"The tender was resent to {recipients.Count} selected supplier(s). The deadline remains {currentDeadline:dd MMM yyyy}.";
        TempData["ToastType"] = "success";
        return RedirectToAction(nameof(Compare), new { tenderId });
    }

    private Task<int> CountResendsAsync(int tenderId, CancellationToken cancellationToken) =>
        _db.AuditLogs.CountAsync(audit => audit.Action == ResendAuditAction
            && audit.EntityName == ResendAuditEntity
            && audit.EntityId == tenderId.ToString(), cancellationToken);

    private Task<int> CountSubmittedSuppliersAsync(int tenderId, CancellationToken cancellationToken) =>
        _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tenderId
                && bid.SubmittedAt.HasValue
                && SubmittedBidStatuses.Contains(bid.Status))
            .Select(bid => bid.SupplierProfileId)
            .Distinct()
            .CountAsync(cancellationToken);

    private async Task<List<ResendSupplier>> GetResendableSuppliersAsync(int tenderId, CancellationToken cancellationToken)
    {
        var selectedSupplierIds = await _db.TenderSuppliers.AsNoTracking()
            .Where(row => row.TenderId == tenderId
                && row.ParticipationStatus != Statuses.ParticipationStatus.Withdrawn)
            .Select(row => row.SupplierProfileId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (selectedSupplierIds.Count == 0) return [];

        var submittedSupplierIds = await _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tenderId
                && bid.SubmittedAt.HasValue
                && SubmittedBidStatuses.Contains(bid.Status))
            .Select(bid => bid.SupplierProfileId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var submittedSet = submittedSupplierIds.ToHashSet();

        var latestInvitationStatuses = await _db.Invitations.AsNoTracking()
            .Where(invitation => invitation.TenderId == tenderId && selectedSupplierIds.Contains(invitation.SupplierProfileId))
            .OrderByDescending(invitation => invitation.SentAt)
            .ThenByDescending(invitation => invitation.Id)
            .Select(invitation => new { invitation.SupplierProfileId, invitation.Status })
            .ToListAsync(cancellationToken);
        var latestStatusBySupplier = latestInvitationStatuses
            .GroupBy(invitation => invitation.SupplierProfileId)
            .ToDictionary(group => group.Key, group => group.First().Status);

        var suppliers = await _db.SupplierProfiles.AsNoTracking()
            .Where(supplier => selectedSupplierIds.Contains(supplier.Id)
                && supplier.Status == Statuses.SupplierStatus.Approved
                && supplier.User.IsActive)
            .Select(supplier => new ResendSupplier(
                supplier.Id, supplier.UserId, supplier.User.Email ?? string.Empty))
            .ToListAsync(cancellationToken);
        return suppliers
            .Where(supplier => !submittedSet.Contains(supplier.SupplierProfileId)
                && (!latestStatusBySupplier.TryGetValue(supplier.SupplierProfileId, out var invitationStatus)
                    || invitationStatus != Statuses.InvitationStatus.Declined))
            .ToList();
    }

    private Task<bool> HasEvaluationStartedAsync(int tenderId, CancellationToken cancellationToken) =>
        _db.Tenders.AsNoTracking().AnyAsync(tender => tender.Id == tenderId
            && (tender.EvaluationReports.Any()
                || tender.Bids.Any(bid => bid.TechnicalEvaluations.Any() || bid.FinancialEvaluations.Any())), cancellationToken);

    private static string? GetResendMessage(
        string tenderStatus, DateTime deadline, int submittedSupplierCount,
        int resendsUsed, int resendableSupplierCount, bool evaluationStarted)
    {
        if (submittedSupplierCount >= MinimumOffersBeforeResend)
            return "At least three suppliers submitted offers. No resend is needed.";
        if (resendsUsed >= MaximumResends)
            return submittedSupplierCount == 0
                ? "No offers were received after three resends. An evaluation cannot start without a submitted offer."
                : $"No more resends. Continue with the {submittedSupplierCount} offer(s) received.";
        if (evaluationStarted)
            return "This tender is already in evaluation, so it cannot be resent.";
        if (tenderStatus is not (Statuses.TenderStatus.Published or Statuses.TenderStatus.Open))
            return "This tender is closed and cannot accept additional offers.";
        if (deadline <= DateTime.UtcNow)
            return "The submission deadline has passed. A resend cannot accept new offers unless the tender deadline is changed.";
        if (resendableSupplierCount == 0)
            return "There are no selected suppliers available to re-invite.";
        return null;
    }

    private sealed record ResendSupplier(int SupplierProfileId, string UserId, string Email);

    private void ValidateFilters(CompareOffersFilters filters, List<string> currencies, List<string> suppliers, int currencyGroupCount)
    {
        if (!string.IsNullOrWhiteSpace(filters.SupplierName) && !suppliers.Contains(filters.SupplierName, StringComparer.Ordinal))
            ModelState.AddModelError("Filters.SupplierName", "Choose a supplier available for this tender.");
        if (!string.IsNullOrWhiteSpace(filters.BidStatus)
            && !EligibleBidStatuses.Contains(filters.BidStatus, StringComparer.Ordinal))
            ModelState.AddModelError("Filters.BidStatus", "Choose an eligible bid status.");

        if (!string.IsNullOrWhiteSpace(filters.ComplianceResult) && filters.ComplianceResult != "__none__"
            && !ValidComplianceResults.Contains(filters.ComplianceResult, StringComparer.Ordinal))
            ModelState.AddModelError("Filters.ComplianceResult", "Choose a listed compliance result.");

        if (!string.IsNullOrWhiteSpace(filters.Currency) && !currencies.Contains(filters.Currency, StringComparer.Ordinal))
            ModelState.AddModelError("Filters.Currency", "Choose a currency available for this tender.");

        if (filters.MinimumPrice is < 0 || filters.MaximumPrice is < 0)
            ModelState.AddModelError("Filters.MinimumPrice", "Price filters must be zero or greater.");
        if (filters.MinimumPrice.HasValue && filters.MaximumPrice.HasValue
            && filters.MinimumPrice.Value > filters.MaximumPrice.Value)
            ModelState.AddModelError("Filters.MaximumPrice", "Maximum price must be greater than or equal to minimum price.");
        if (filters.MaximumDeliveryDays is < 1)
            ModelState.AddModelError("Filters.MaximumDeliveryDays", "Maximum delivery days must be a positive number.");
        if (filters.MinimumValidityDays is < 1)
            ModelState.AddModelError("Filters.MinimumValidityDays", "Minimum validity days must be a positive number.");
        if (filters.SubmittedFrom.HasValue && filters.SubmittedTo.HasValue
            && filters.SubmittedFrom.Value.Date > filters.SubmittedTo.Value.Date)
            ModelState.AddModelError("Filters.SubmittedTo", "The end date must be on or after the start date.");
        if (currencyGroupCount > 1 && string.IsNullOrWhiteSpace(filters.Currency)
            && (filters.MinimumPrice.HasValue || filters.MaximumPrice.HasValue))
            ModelState.AddModelError("Filters.Currency", "Choose a currency before filtering by price because offers use different currencies.");
        if (!ValidSortFields.Contains(filters.SortBy, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Filters.SortBy", "Choose a supported sort field.");
            filters.SortBy = "submitted";
        }

    }

    private static IQueryable<Bid> ApplySort(IQueryable<Bid> bids, string sortBy, bool descending, bool multipleCurrencies)
    {
        IOrderedQueryable<Bid> ordered = sortBy switch
        {
            "supplier" => descending
                ? bids.OrderByDescending(bid => bid.SupplierProfile.CompanyName)
                : bids.OrderBy(bid => bid.SupplierProfile.CompanyName),
            "price" when multipleCurrencies => descending
                ? bids.OrderBy(bid => bid.Currency).ThenByDescending(bid => bid.TotalPrice)
                : bids.OrderBy(bid => bid.Currency).ThenBy(bid => bid.TotalPrice),
            "price" => descending ? bids.OrderByDescending(bid => bid.TotalPrice) : bids.OrderBy(bid => bid.TotalPrice),
            "submitted" => descending ? bids.OrderByDescending(bid => bid.SubmittedAt) : bids.OrderBy(bid => bid.SubmittedAt),
            "delivery" => descending
                ? bids.OrderBy(bid => bid.DeliveryDays <= 0 ? 1 : 0).ThenByDescending(bid => bid.DeliveryDays)
                : bids.OrderBy(bid => bid.DeliveryDays <= 0 ? 1 : 0).ThenBy(bid => bid.DeliveryDays),
            "validity" => descending
                ? bids.OrderBy(bid => bid.ValidityDays <= 0 ? 1 : 0).ThenByDescending(bid => bid.ValidityDays)
                : bids.OrderBy(bid => bid.ValidityDays <= 0 ? 1 : 0).ThenBy(bid => bid.ValidityDays),
            "compliance" => descending
                ? bids.OrderBy(bid => bid.ComplianceChecks.Any() ? 0 : 1)
                    .ThenByDescending(bid => bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                        .ThenByDescending(check => check.Id).Select(check => check.Result).FirstOrDefault())
                : bids.OrderBy(bid => bid.ComplianceChecks.Any() ? 0 : 1)
                    .ThenBy(bid => bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                        .ThenByDescending(check => check.Id).Select(check => check.Result).FirstOrDefault()),
            _ => descending ? bids.OrderByDescending(bid => bid.SubmittedAt) : bids.OrderBy(bid => bid.SubmittedAt)
        };

        return ordered.ThenBy(bid => bid.Id);
    }
}
