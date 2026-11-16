namespace TenderSystem.Areas.Manager.Models;

public class ManagerReportReviewViewModel
{
    public int TenderId { get; set; }
    public int RequisitionId { get; set; }
    public int ReportId { get; set; }
    public int? RecommendedBidId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public string RequestReason { get; set; } = string.Empty;
    public string RequestPriority { get; set; } = string.Empty;
    public DateTime RequestTargetDate { get; set; }
    public string TenderDescription { get; set; } = string.Empty;
    public string TenderTerms { get; set; } = string.Empty;
    public DateTime? PublishDate { get; set; }
    public DateTime SubmissionDeadline { get; set; }
    public string PreparedByName { get; set; } = string.Empty;
    public decimal EstimatedValue { get; set; }
    public bool RequiresTopCommittee { get; set; }
    public string ReportStatus { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string TechnicalSummary { get; set; } = string.Empty;
    public string FinancialSummary { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
    public string? RecommendedSupplier { get; set; }
    public decimal? RecommendedBidPrice { get; set; }
    public string? RecommendedBidCurrency { get; set; }
    public List<ManagerReviewRequestItemViewModel> RequestItems { get; set; } = [];
    public List<ManagerReviewCommitteeMemberViewModel> CommitteeMembers { get; set; } = [];
    public List<ManagerReviewBidViewModel> Bids { get; set; } = [];
    public List<ManagerAiAnalysisViewModel> AiAnalyses { get; set; } = [];
    public List<ManagerClarificationThreadViewModel> Clarifications { get; set; } = [];
}
