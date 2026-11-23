namespace TenderSystem.Areas.Sourcing.Models;

public sealed class CompliancePageViewModel
{
    public List<TenderOption> Tenders { get; init; } = [];
    public List<ExtractedRequirement> Requirements { get; init; } = [];
    public int? SelectedTenderId { get; init; }
    public TenderDetails? SelectedTender { get; init; }
    public List<BidRow> Bids { get; init; } = [];
    public BidDetails? SelectedBid { get; init; }
    public ComplianceDecisionInputViewModel Decision { get; init; } = new();

    public sealed record TenderOption(int Id, string TenderNumber, string Title, string Status, DateTime CreatedAt, DateTime Deadline);

    public sealed record TenderDetails(
        int Id, int RequisitionId, string TenderNumber, string Title, string Description, string Terms,
        string Status, DateTime SubmissionDeadline);

    public sealed record BidRow(
        int Id, int SupplierProfileId, string SupplierName, DateTime SubmittedAt,
        string Status, string? LatestComplianceResult);

    public sealed record BidDetails(
        int Id, int TenderId, string SupplierName, string Status, DateTime SubmittedAt,
        int SupplierProfileId, string SupplierTaxNumber, string CommercialRegNumber, string SupplierStatus,
        int DeliveryDays, int ValidityDays, string PaymentTerms)
    {
        public List<ItemCoverageRow> ItemCoverage { get; init; } = [];
        public List<SupplierDocumentRow> SupplierDocuments { get; init; } = [];
        public List<GuaranteeRow> Guarantees { get; init; } = [];
        public List<ReviewRow> Reviews { get; init; } = [];
    }

    public sealed record ItemCoverageRow(
        string ItemName, decimal RequestedQuantity, string Unit, string RequestedSpecifications,
        decimal? OfferedQuantity, string OfferedSpecifications, string Status);

    public sealed record DocumentRow(int Id, string DocType, string FileName, DateTime UploadedAt, long SizeBytes, int? BidGuaranteeId = null);

    public sealed record SupplierDocumentRow(
        int Id, string DocType, DateTime? ExpiryDate, DateTime UploadedAt, bool CanPreview, bool IsExpired);

    public sealed record GuaranteeRow(
        int Id, string GuaranteeType, string? ReferenceNumber, decimal Amount, string Currency,
        DateTime? ExpiresAt, bool IsExpired, List<DocumentRow> Documents);

    public sealed record ReviewRow(string Result, string ReviewerName, DateTime CheckedAt, string? Reason);

    public sealed record ExtractedRequirement(
        string Name,
        string SourceText,
        string ReviewClassification,
        List<DocumentRow> RelatedDocuments);
}
