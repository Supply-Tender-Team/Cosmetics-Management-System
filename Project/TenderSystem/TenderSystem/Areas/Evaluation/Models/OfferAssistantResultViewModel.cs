namespace TenderSystem.Areas.Evaluation.Models;

// Populated only after an authorized service response has been validated on the server.
public class OfferAssistantResultViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public int SuggestedBidId { get; set; }
    public string SuggestedSupplier { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<string> Checks { get; set; } = new();
    public List<string> Alternatives { get; set; } = new();
    public string ScopeNote { get; set; } = string.Empty;
}
