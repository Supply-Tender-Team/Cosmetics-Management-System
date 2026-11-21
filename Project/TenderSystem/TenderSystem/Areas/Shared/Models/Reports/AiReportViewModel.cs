namespace TenderSystem.Areas.Shared.Models.Reports;

// Presentation contract for the future service adapter. Values are plain text, never HTML.
public class AiReportViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int RecordCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ReportFieldViewModel> KeyFigures { get; set; } = new();
    public List<string> Findings { get; set; } = new();
    public List<string> NeedsAttention { get; set; } = new();
    public List<ReportFieldViewModel> Details { get; set; } = new();
}
