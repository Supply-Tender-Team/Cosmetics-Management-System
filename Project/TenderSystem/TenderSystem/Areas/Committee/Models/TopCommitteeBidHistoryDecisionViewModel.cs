namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidHistoryDecisionViewModel
{
    public string Stage { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string DecidedBy { get; set; } = string.Empty;
    public string? SelectedSupplier { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime DecidedAt { get; set; }
}
