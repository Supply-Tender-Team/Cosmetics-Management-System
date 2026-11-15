using TenderSystem.Services.Ai;

namespace TenderSystem.Areas.Evaluation.Models;

public class AiAnalysisViewModel
{
    public List<EvaluationTenderViewModel> Tenders { get; set; } = new();
    public int? TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<AiBidAnalysisItem> Items { get; set; } = Array.Empty<AiBidAnalysisItem>();
    public Dictionary<int, string> BidStatuses { get; set; } = new();
}
