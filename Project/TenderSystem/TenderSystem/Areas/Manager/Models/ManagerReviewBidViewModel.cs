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
    public decimal? CommitteeTechnicalAverage { get; set; }
    public decimal? CommitteeFinancialAverage { get; set; }
    public decimal? CommitteeTotalAverage { get; set; }
    public List<ManagerReviewBidItemViewModel> Items { get; set; } = [];
    public List<ManagerReviewEvaluatorResultViewModel> Evaluations { get; set; } = [];
    public List<string> ComplianceNotes { get; set; } = [];
    public List<ManagerReviewBidDocumentViewModel> Documents { get; set; } = [];
}
