namespace TenderSystem.Areas.Sourcing.Models;

public sealed record EvaluationTrackingViewModel
{
    public List<TenderOption> Tenders { get; init; } = [];
    public int? SelectedTenderId { get; init; }
    public TenderDetails? SelectedTender { get; init; }
    public TrackingFilters Filters { get; init; } = new();
    public SummaryStatistics Statistics { get; init; } = new(0, 0, 0, 0, 0, 0);
    public int MatchingBidCount { get; init; }
    public List<StageProgress> Stages { get; init; } = [];
    public List<CommitteeProgress> CommitteeMembers { get; init; } = [];
    public EvaluationReportDetails? LatestReport { get; init; }
    public List<BidProgress> Bids { get; init; } = [];
    public string? Notice { get; init; }

    public sealed record TenderOption(
        int Id, string TenderNumber, string Title, string Status, DateTime Deadline,
        int SubmittedBids, string LatestReportStatus);

    public sealed record TenderDetails(
        int Id, string TenderNumber, string Title, string Status, DateTime Deadline,
        int SubmittedBidCount, int EligibleBidCount, int TechnicalCriteriaCount, int FinancialCriteriaCount);

    public sealed record SummaryStatistics(
        int ActiveTenderCount,
        int EligibleBidCount,
        int CompletedBidEvaluations,
        int InProgressBidEvaluations,
        int PendingBidEvaluations,
        int ReportsAwaitingManagerReview);

    public sealed record StageProgress(
        string Name, string Responsible, string Status, int CompletedCount,
        int TotalCount, DateTime? LastUpdated, string Details);

    public sealed record CommitteeProgress(
        string UserId, string Name, string Role, string Activity,
        string Status, DateTime? LastUpdated, string? Note);

    public sealed record EvaluationReportDetails(
        int Id, string Status, string StatusLabel, DateTime CreatedAt,
        DateTime? SubmittedAt, string PreparedBy, string? RecommendedSupplier,
        int? RecommendedBidId, decimal? RecommendedPrice, string? RecommendedCurrency,
        int? RecommendedDeliveryDays, int? RecommendedValidityDays, string? RecommendedPaymentTerms,
        string TechnicalSummary, string FinancialSummary, string Justification,
        string ManagerStatus, DateTime? ManagerDecisionAt, string? ManagerComment);

    public sealed record BidProgress(
        int BidId, string SupplierName, DateTime SubmittedAt, string BidStatus,
        string? ComplianceResult, bool IsEligible,
        string TechnicalStatus, int TechnicalCompletedCriteria, int TechnicalRequiredCriteria,
        DateTime? TechnicalLastUpdated,
        string FinancialStatus, bool HasFinancialEvaluation, DateTime? FinancialLastUpdated,
        string OverallStatus, string ReportStatus, string EligibilityNote, string RejectionReason);
}

public sealed class TrackingFilters
{
    public string? SupplierSearch { get; set; }
    public string? EvaluationStatus { get; set; }
    public string? CommitteeMemberId { get; set; }
    public string? ReportStatus { get; set; }
}
