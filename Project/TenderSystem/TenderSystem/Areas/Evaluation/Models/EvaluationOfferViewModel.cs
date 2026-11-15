namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationOfferViewModel
{
    public bool HasSavedEvaluation { get; set; }
    public bool? IsLowestPrice { get; set; }
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string PaymentTerms { get; set; } = string.Empty;
    public string TechnicalSummary { get; set; } = string.Empty;
}
