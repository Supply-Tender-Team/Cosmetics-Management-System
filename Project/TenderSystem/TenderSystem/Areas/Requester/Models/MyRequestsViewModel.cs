namespace TenderSystem.Areas.Requester.Models;

public class MyRequestsViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<PurchaseRequestDetailsViewModel> Requests { get; set; } = [];
}
