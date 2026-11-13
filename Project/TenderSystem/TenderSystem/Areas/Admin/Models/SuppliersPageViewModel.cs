namespace TenderSystem.Areas.Admin.Models;

public class SuppliersPageViewModel
{
    public string? Search { get; set; }
    public string Status { get; set; } = "All";
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int SuspendedCount { get; set; }
    public List<SupplierRowViewModel> Suppliers { get; set; } = [];
}
