namespace TenderSystem.Areas.Manager.Models;

public class ManagerReviewFinancialScoreViewModel
{
    public decimal? Score { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TaxesAndFees { get; set; }
    public decimal LogisticsCost { get; set; }
    public string? Notes { get; set; }
    public DateTime? ReadyAt { get; set; }
}
