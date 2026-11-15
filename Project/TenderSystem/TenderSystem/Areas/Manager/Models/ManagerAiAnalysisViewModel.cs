namespace TenderSystem.Areas.Manager.Models;

public class ManagerAiAnalysisViewModel
{
    public string SupplierName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public decimal? ExtractedPrice { get; set; }
    public int? ExtractedDeliveryDays { get; set; }
    public string? MissingFiles { get; set; }
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? Recommendation { get; set; }
    public DateTime CreatedAt { get; set; }
}
