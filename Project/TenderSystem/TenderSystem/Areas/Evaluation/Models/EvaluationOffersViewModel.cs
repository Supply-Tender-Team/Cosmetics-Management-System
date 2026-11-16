namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationOffersViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CommitteeRecommendationViewModel? Recommendation { get; set; }
    public List<EvaluationOfferViewModel> Offers { get; set; } = new List<EvaluationOfferViewModel>();
}
