namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationFormViewModel
{
    public int SavedMemberCount { get; set; }
    public decimal? AverageSavedTechnicalPoints { get; set; }
    public List<EvaluationOfferViewModel> ComparisonOffers { get; set; } = new List<EvaluationOfferViewModel>();
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string EvaluatorName { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public string? ConfigurationMessage { get; set; }
    public string? FinancialMessage { get; set; }
    public decimal TechnicalWeight { get; set; }
    public decimal FinancialWeight { get; set; }
    public decimal? TechnicalPoints { get; set; }
    public decimal? FinancialPoints { get; set; }
    public decimal? SavedFinancialPoints { get; set; }
    public bool? IsLowestPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string TechnicalSummary { get; set; } = string.Empty;
    public SaveEvaluationViewModel Input { get; set; } = new SaveEvaluationViewModel();
    public List<EvaluationMemberViewModel> Members { get; set; } = new List<EvaluationMemberViewModel>();
    public List<EvaluationItemViewModel> Items { get; set; } = new List<EvaluationItemViewModel>();
    public List<EvaluationDocumentViewModel> Documents { get; set; } = new List<EvaluationDocumentViewModel>();
}
