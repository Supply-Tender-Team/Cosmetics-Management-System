namespace TenderSystem.Areas.Evaluation.Models;

public class ReportOfferViewModel
{
    public int BidId { get; set; }
    public string Supplier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Eligible { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Dictionary<string, decimal?> MemberPoints { get; set; } = new();
    public decimal? TechnicalPoints { get; set; }
    public decimal? FinancialPoints { get; set; }
    public decimal? TotalPoints => TechnicalPoints + FinancialPoints;
    public List<string> Notes { get; set; } = new();
}
