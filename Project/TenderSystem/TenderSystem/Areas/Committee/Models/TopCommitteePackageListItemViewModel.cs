namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteePackageListItemViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public decimal EstimatedValue { get; set; }
    public int BidCount { get; set; }
    public string RecommendedSupplier { get; set; } = string.Empty;
    public decimal? RecommendedPrice { get; set; }
    public string? RecommendedCurrency { get; set; }
    public string ManagerName { get; set; } = string.Empty;
    public string ManagerComment { get; set; } = string.Empty;
    public DateTime ManagerApprovedAt { get; set; }
}
