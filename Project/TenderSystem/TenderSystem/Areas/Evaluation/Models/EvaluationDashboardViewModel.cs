namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationDashboardViewModel
{
    public int AssignedTenderCount { get; set; }
    public int UnderEvaluationCount { get; set; }
    public int EligibleOfferCount { get; set; }
    public int AwaitingReviewCount { get; set; }
    public string? Search { get; set; }
    public List<EvaluationTenderViewModel> Tenders { get; set; } = new List<EvaluationTenderViewModel>();
}
