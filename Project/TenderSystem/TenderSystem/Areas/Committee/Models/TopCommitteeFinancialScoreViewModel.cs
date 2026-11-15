namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeFinancialScoreViewModel
{
    public decimal Score { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TaxesAndFees { get; set; }
    public decimal LogisticsCost { get; set; }
    public string? Notes { get; set; }
}
