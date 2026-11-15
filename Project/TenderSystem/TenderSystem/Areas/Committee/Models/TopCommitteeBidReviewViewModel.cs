namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidReviewViewModel
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string PaymentTerms { get; set; } = string.Empty;
    public string SupplierSummary { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public List<TopCommitteeBidItemViewModel> Items { get; set; } = [];
    public List<TopCommitteeTechnicalScoreViewModel> TechnicalScores { get; set; } = [];
    public List<TopCommitteeFinancialScoreViewModel> FinancialScores { get; set; } = [];
    public List<string> ComplianceResults { get; set; } = [];
    public List<string> Documents { get; set; } = [];
}
