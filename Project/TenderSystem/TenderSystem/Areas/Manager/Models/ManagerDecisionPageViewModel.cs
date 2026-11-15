namespace TenderSystem.Areas.Manager.Models;

public class ManagerDecisionPageViewModel
{
    public int TenderId { get; set; }
    public int ReportId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string ReportStatus { get; set; } = string.Empty;
    public decimal EstimatedValue { get; set; }
    public bool RequiresTopCommittee { get; set; }
    public string? RecommendedSupplier { get; set; }
    public decimal? RecommendedBidPrice { get; set; }
    public string? RecommendedBidCurrency { get; set; }
    public ManagerDecisionInputViewModel Input { get; set; } = new();
}
