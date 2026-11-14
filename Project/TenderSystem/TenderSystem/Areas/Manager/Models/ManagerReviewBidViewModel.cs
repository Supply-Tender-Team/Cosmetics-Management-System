namespace TenderSystem.Areas.Manager.Models;

public class ManagerReviewBidViewModel
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string PaymentTerms { get; set; } = string.Empty;
    public string TechnicalSummary { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public List<ManagerReviewTechnicalScoreViewModel> TechnicalScores { get; set; } = [];
    public List<ManagerReviewFinancialScoreViewModel> FinancialScores { get; set; } = [];
}
