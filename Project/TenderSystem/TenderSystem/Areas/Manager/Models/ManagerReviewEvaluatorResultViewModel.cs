namespace TenderSystem.Areas.Manager.Models;

public class ManagerReviewEvaluatorResultViewModel
{
    public string EvaluatorName { get; set; } = string.Empty;
    public string CommitteeRole { get; set; } = string.Empty;
    public decimal? TechnicalPoints { get; set; }
    public ManagerReviewFinancialScoreViewModel? FinancialScore { get; set; }
    public List<ManagerReviewTechnicalScoreViewModel> TechnicalScores { get; set; } = [];
}
