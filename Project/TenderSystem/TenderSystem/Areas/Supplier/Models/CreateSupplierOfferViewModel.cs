using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Supplier.Models;

public sealed class CreateSupplierOfferViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public DateTime SubmissionDeadline { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = "USD";

    [Range(0, 3650)]
    public int DeliveryDays { get; set; }

    [Range(0, 3650)]
    public int ValidityDays { get; set; }

    [StringLength(300)]
    public string PaymentTerms { get; set; } = string.Empty;

    [StringLength(4000)]
    public string TechnicalSummary { get; set; } = string.Empty;

    public List<SupplierOfferItemInputViewModel> Items { get; set; } = [];
}
