namespace TenderSystem.Areas.Manager.Models;

public class ManagerReviewTechnicalScoreViewModel
{
    public string CriterionName { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public int MaxScore { get; set; }
    public decimal? Score { get; set; }
    public string? Notes { get; set; }
}
