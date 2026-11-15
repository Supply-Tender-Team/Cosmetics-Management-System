using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class OffersController : Controller
{
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

    private readonly ApplicationDbContext _db;

    public OffersController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Compare(
        int? tenderId,
        CompareOffersFilters? filters,
        CancellationToken cancellationToken)
    {
        filters ??= new CompareOffersFilters();
        filters.SupplierName = filters.SupplierName?.Trim();
        filters.Currency = filters.Currency?.Trim();

        var tenderQuery = _db.Tenders.AsNoTracking()
            .Where(tender => tender.Bids.Any(bid => bid.SubmittedAt.HasValue
                && EligibleBidStatuses.Contains(bid.Status)));

        var tenders = await tenderQuery
            .OrderByDescending(tender => tender.SubmissionDeadline)
            .ThenByDescending(tender => tender.Id)
            .Select(tender => new CompareOffersViewModel.TenderOption(
                tender.Id,
                tender.TenderNumber,
                tender.Title,
                tender.Status,
                tender.SubmissionDeadline,
                tender.Bids.Count(bid => bid.SubmittedAt.HasValue && EligibleBidStatuses.Contains(bid.Status))))
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
                tender.Id, tender.TenderNumber, tender.Title, tender.Status, tender.SubmissionDeadline))
            .SingleOrDefaultAsync(cancellationToken);

        if (selectedTender is null)
        {
            return View(model with { PageMessage = "The selected tender could not be found. Choose an available tender below." });
        }

        model = model with { SelectedTender = selectedTender };

        var eligibleBids = _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tenderId.Value
                && bid.SubmittedAt.HasValue
                && EligibleBidStatuses.Contains(bid.Status));

        var eligibleCount = await eligibleBids.CountAsync(cancellationToken);
        if (eligibleCount == 0)
        {
            return View(model with { PageMessage = "This tender has no eligible submitted offers to compare." });
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
            Offers = offerRows
        });
    }

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
