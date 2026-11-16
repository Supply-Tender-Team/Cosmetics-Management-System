namespace TenderSystem.Areas.Manager.Models;

public class ManagerReviewBidItemViewModel
{
    public string ItemName { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal OfferedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal? InstallationCost { get; set; }
    public string OfferedSpecifications { get; set; } = string.Empty;
}
