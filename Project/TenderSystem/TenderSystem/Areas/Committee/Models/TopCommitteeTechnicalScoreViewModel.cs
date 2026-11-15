namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeTechnicalScoreViewModel
{
    public string CriterionName { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public decimal Score { get; set; }
    public int MaxScore { get; set; }
    public string? Notes { get; set; }
}
