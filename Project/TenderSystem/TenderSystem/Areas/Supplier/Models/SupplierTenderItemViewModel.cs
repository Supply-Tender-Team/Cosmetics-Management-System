namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierTenderItemViewModel
{
    public string ItemName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string Specifications { get; init; } = string.Empty;
}
