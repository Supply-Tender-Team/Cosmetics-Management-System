namespace TenderSystem.Areas.Requester.Models;

public class PurchaseRequestDetailsItemViewModel
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Specifications { get; set; } = string.Empty;
}
