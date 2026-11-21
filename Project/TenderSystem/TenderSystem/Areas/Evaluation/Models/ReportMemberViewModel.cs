namespace TenderSystem.Areas.Evaluation.Models;

public class ReportMemberViewModel
{
    public string Name { get; set; } = string.Empty;
    public decimal? Points { get; set; }
    public decimal? FinancialPoints { get; set; }
    public bool Ready { get; set; }
    public Dictionary<string, List<string>> Comments { get; set; } = new();
}
