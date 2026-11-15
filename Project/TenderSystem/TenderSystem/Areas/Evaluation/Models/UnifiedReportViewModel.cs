using TenderSystem.Services.Ai;

namespace TenderSystem.Areas.Evaluation.Models;

public class UnifiedReportViewModel
{
    public List<EvaluationTenderViewModel> Tenders { get; set; } = new();
    public int? TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public List<ReportOfferViewModel> Offers { get; set; } = new();
    public decimal TechnicalWeight { get; set; }
    public bool AllReady { get; set; }
    public int ReadyFormCount { get; set; }
    public int RequiredFormCount { get; set; }
    public string? PendingMessage { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string TechnicalSnapshot { get; set; } = string.Empty;
    public string FinancialSnapshot { get; set; } = string.Empty;
    public string SubmittedJustification { get; set; } = string.Empty;
    public IReadOnlyList<AiBidAnalysisItem> AiItems { get; set; } = Array.Empty<AiBidAnalysisItem>();
}
