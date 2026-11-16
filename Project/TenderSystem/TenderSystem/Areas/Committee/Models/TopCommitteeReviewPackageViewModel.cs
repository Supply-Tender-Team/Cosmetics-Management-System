namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeReviewPackageViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public decimal EstimatedValue { get; set; }
    public DateTime SubmissionDeadline { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime TargetDate { get; set; }
    public string RequestReason { get; set; } = string.Empty;
    public decimal? EstimatedBudget { get; set; }
    public string ReportStatus { get; set; } = string.Empty;
    public DateTime? ReportSubmittedAt { get; set; }
    public string TechnicalSummary { get; set; } = string.Empty;
    public string FinancialSummary { get; set; } = string.Empty;
    public string RecommendationJustification { get; set; } = string.Empty;
    public string RecommendedSupplier { get; set; } = string.Empty;
    public int? ManagerSelectedBidId { get; set; }
    public decimal? RecommendedPrice { get; set; }
    public string? RecommendedCurrency { get; set; }
    public string ManagerName { get; set; } = string.Empty;
    public string ManagerComment { get; set; } = string.Empty;
    public string ManagerNote { get; set; } = string.Empty;
    public DateTime ManagerApprovedAt { get; set; }
    public List<TopCommitteeRequestItemViewModel> RequestedItems { get; set; } = [];
    public List<TopCommitteeBidReviewViewModel> Bids { get; set; } = [];
    public List<TopCommitteeAiAnalysisViewModel> AiAnalyses { get; set; } = [];
}
