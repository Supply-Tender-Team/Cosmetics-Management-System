namespace TenderSystem.Areas.Evaluation.Models;

public class RecommendationOfferViewModel
{
    public int BidId { get; set; }
    public string Supplier { get; set; } = string.Empty;
    public bool HasSavedEvaluation { get; set; }
    public decimal? MyTechnicalPoints { get; set; }
    public decimal? FinancialPoints { get; set; }
    public decimal? CommitteeTotal { get; set; }
    public decimal? MyTotal => MyTechnicalPoints + FinancialPoints;
}
