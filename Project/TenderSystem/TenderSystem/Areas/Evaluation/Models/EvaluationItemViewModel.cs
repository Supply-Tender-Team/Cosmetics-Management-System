namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public string RequiredSpecifications { get; set; } = string.Empty;
    public string OfferedSpecifications { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal InstallationCost { get; set; }
}
