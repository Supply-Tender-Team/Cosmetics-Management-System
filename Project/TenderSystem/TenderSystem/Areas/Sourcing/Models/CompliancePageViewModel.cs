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

    public sealed record TenderOption(int Id, string TenderNumber, string Title, string Status, DateTime Deadline);

    public sealed record TenderDetails(
        int Id, string TenderNumber, string Title, string Description, string Terms,
        string Status, DateTime SubmissionDeadline);

    public sealed record BidRow(
        int Id, string SupplierName, DateTime SubmittedAt, decimal TotalPrice,
        string Currency, string Status, string? LatestComplianceResult);

    public sealed record BidDetails(
        int Id, int TenderId, string SupplierName, string SupplierCategory, string SupplierCountry,
        string Status, decimal TotalPrice, string Currency, int DeliveryDays, int ValidityDays,
        string PaymentTerms, string TechnicalSummary, DateTime SubmittedAt)
    {
        public List<DocumentRow> Documents { get; init; } = [];
        public List<ReviewRow> Reviews { get; init; } = [];
    }

    public sealed record DocumentRow(int Id, string DocType, string FileName, DateTime UploadedAt, long SizeBytes);

    public sealed record ReviewRow(string Result, string ReviewerName, DateTime CheckedAt, string? Reason);

    public sealed record ExtractedRequirement(
        string Name,
        string SourceText,
        string ReviewClassification,
        List<DocumentRow> RelatedDocuments);
}
