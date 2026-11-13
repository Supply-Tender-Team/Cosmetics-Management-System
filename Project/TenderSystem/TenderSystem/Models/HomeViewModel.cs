namespace TenderSystem.Models;

/// <summary>Real numbers shown on the public landing page.</summary>
public class HomeViewModel
{
    public int ActiveTenders { get; set; }
    public int ApprovedSuppliers { get; set; }
    public int CompletedRequests { get; set; }
    public int AwardedTenders { get; set; }
}
