namespace TenderSystem.Areas.Sourcing.Models;

public sealed record CompareOffersViewModel
{
    public List<TenderOption> Tenders { get; init; } = [];
    public int? SelectedTenderId { get; init; }
    public TenderDetails? SelectedTender { get; init; }
    public CompareOffersFilters Filters { get; init; } = new();
    public List<string> FilterErrors { get; init; } = [];
    public string? PageMessage { get; init; }
    public int EligibleOfferCount { get; init; }
    public int MatchingOfferCount { get; init; }
    public int SupplierCount { get; init; }
    public int? EarliestDeliveryDays { get; init; }
    public int? LatestDeliveryDays { get; init; }
    public int SubmittedSupplierCount { get; init; }
    public int ResendsUsed { get; init; }
    public int MaxResends { get; init; }
    public int ResendableSupplierCount { get; init; }
    public bool CanResend { get; init; }
    public string? ResendMessage { get; init; }
    public List<CurrencySummary> CurrencySummaries { get; init; } = [];
    public List<string> AvailableCurrencies { get; init; } = [];
    public List<string> AvailableSuppliers { get; init; } = [];
    public List<OfferRow> Offers { get; init; } = [];

    public sealed record TenderOption(int Id, string TenderNumber, string Title, string Status, DateTime SubmissionDeadline, int EligibleOfferCount);

    public sealed record TenderDetails(
        int Id, string TenderNumber, string Title, string Status, DateTime SubmissionDeadline,
        int SubmittedSupplierCount = 0, int ResendsUsed = 0, int ResendableSupplierCount = 0,
        bool CanResend = false, string? ResendMessage = null);

    public sealed record CurrencySummary(string Currency, int OfferCount, decimal LowestPrice, decimal HighestPrice, decimal AveragePrice);

    public sealed record OfferRow(
        int Id,
        string SupplierName,
        DateTime SubmittedAt,
        decimal TotalPrice,
        string Currency,
        int DeliveryDays,
        int ValidityDays,
        string PaymentTerms,
        string TechnicalSummary,
        string Status,
        string? LatestComplianceResult,
        int DocumentCount,
        List<BidItemRow> Items,
        List<DocumentRow> Documents);

    public sealed record BidItemRow(
        string ItemName,
        string Unit,
        decimal Quantity,
        decimal UnitPrice,
        decimal TaxAmount,
        decimal ShippingCost,
        decimal? InstallationCost,
        string OfferedSpecifications);

    public sealed record DocumentRow(int Id, string DocType, string FileName, DateTime UploadedAt, long SizeBytes);
}

public sealed class CompareOffersFilters
{
    public string? SupplierName { get; set; }
    public string? BidStatus { get; set; }
    public string? ComplianceResult { get; set; }
    public string? Currency { get; set; }
    public decimal? MinimumPrice { get; set; }
    public decimal? MaximumPrice { get; set; }
    public int? MaximumDeliveryDays { get; set; }
    public int? MinimumValidityDays { get; set; }
    public bool? HasDocuments { get; set; }
    public DateTime? SubmittedFrom { get; set; }
    public DateTime? SubmittedTo { get; set; }
    public string SortBy { get; set; } = "submitted";
    public bool Descending { get; set; } = true;
}
