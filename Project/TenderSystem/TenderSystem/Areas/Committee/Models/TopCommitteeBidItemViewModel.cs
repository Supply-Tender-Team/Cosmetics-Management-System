namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidItemViewModel
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal? InstallationCost { get; set; }
    public string OfferedSpecifications { get; set; } = string.Empty;
}
