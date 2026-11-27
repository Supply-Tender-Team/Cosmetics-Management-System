namespace TenderSystem.Areas.Sourcing.Models;

public sealed record EvaluationTrackingViewModel
{
    public List<TenderOption> Tenders { get; init; } = new List<TenderOption>();
    public int? SelectedTenderId { get; init; }
    public TenderDetails? SelectedTender { get; init; }
    public TrackingFilters Filters { get; init; } = new TrackingFilters();
    public SummaryStatistics Statistics { get; init; } = new SummaryStatistics(0, 0, 0, 0, 0, 0);
    public int MatchingBidCount { get; init; }
    public List<StageProgress> Stages { get; init; } = new List<StageProgress>();
    public List<EvaluationCriterionRow> EvaluationCriteria { get; init; } = new List<EvaluationCriterionRow>();
    public List<CommitteeProgress> CommitteeMembers { get; init; } = new List<CommitteeProgress>();
    public EvaluationReportDetails? LatestReport { get; init; }
    public bool CanManageSupplierResults { get; init; }
    public bool SupplierResultsSent { get; init; }
    public bool CanSendReportToManager { get; init; }
    public int ClarificationThreadCount { get; init; }
    public int ReadyConfirmationsCompleted { get; init; }
    public int ReadyConfirmationsRequired { get; init; }
    public string? ReportReadinessMessage { get; init; }
    public List<BidProgress> Bids { get; init; } = new List<BidProgress>();
    public List<string> AvailableSuppliers { get; init; } = new List<string>();
    public string? Notice { get; init; }

    public sealed record TenderOption(
        int Id, string TenderNumber, string Title, string Status, DateTime Deadline,
        int SubmittedBids, string LatestReportStatus);

    public sealed record TenderDetails(
        int Id, string TenderNumber, string Title, string Status, DateTime Deadline,
        int SubmittedBidCount, int EligibleBidCount, int TechnicalCriteriaCount, int FinancialCriteriaCount,
        decimal TechnicalWeight, decimal FinancialWeight,
        int? AwardedSupplierId, string? AwardedSupplierName);

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

    public sealed record EvaluationCriterionRow(
        int Id, string Name, string Type, decimal WeightPercent, int MaxScore);

    public sealed record CommitteeProgress(
        string UserId, string Name, string Role, string Activity);

    public sealed record EvaluationReportDetails(
        int Id, string Status, string StatusLabel, DateTime CreatedAt,
        DateTime? SubmittedAt, string PreparedBy, string? RecommendedSupplier,
        int? RecommendedBidId, decimal? RecommendedPrice, string? RecommendedCurrency,
        int? RecommendedDeliveryDays, int? RecommendedValidityDays, string? RecommendedPaymentTerms,
        string TechnicalSummary, string FinancialSummary, string Justification,
        string ManagerStatus, DateTime? ManagerDecisionAt, string? ManagerComment,
        string? ManagerSelectedSupplier);

    public sealed record BidProgress(
        int BidId, string SupplierName, DateTime SubmittedAt, string BidStatus,
        string? ComplianceResult, bool IsEligible,
        string TechnicalStatus, int TechnicalCompletedCriteria, int TechnicalRequiredCriteria,
        int TechnicalCompletedMembers, int RequiredCommitteeMembers,
        decimal? TechnicalAveragePoints, decimal? TechnicalPercentage,
        DateTime? TechnicalLastUpdated,
        string FinancialStatus, bool HasFinancialEvaluation, int FinancialReadyMembers,
        decimal? FinancialAveragePoints, decimal? FinancialPercentage, DateTime? FinancialLastUpdated,
        string OverallStatus, string ReportStatus, string EligibilityNote, string RejectionReason,
        string OfferOutcome);
}

public sealed class TrackingFilters
{
    public string? SupplierSearch { get; set; }
    public string? EvaluationStatus { get; set; }
    public string? CommitteeMemberId { get; set; }
    public string? ReportStatus { get; set; }
}
