namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeRequestItemViewModel
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Specifications { get; set; } = string.Empty;
}
