namespace TenderSystem.Areas.Requester.Models;

public class PurchaseRequestDetailsViewModel
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime TargetDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal? EstimatedBudget { get; set; }
    public string? ReviewNote { get; set; }
    public List<PurchaseRequestDetailsItemViewModel> Items { get; set; } = [];
}
