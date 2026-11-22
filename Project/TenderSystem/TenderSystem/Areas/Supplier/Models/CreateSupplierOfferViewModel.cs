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

    public List<SupplierOfferGuaranteeInputViewModel> Guarantees { get; set; } = [];

    public List<SupplierOfferExistingDocumentViewModel> ExistingDocuments { get; set; } = [];
}

public sealed class SupplierOfferGuaranteeInputViewModel
{
    public int? BidGuaranteeId { get; set; }
    public string GuaranteeType { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? IssuerName { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "JOD";
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? GuarantorName { get; set; }
    public string? GuarantorNationalNumber { get; set; }
    public string? GuarantorAddress { get; set; }
    public string? GuarantorPhone { get; set; }
    public IFormFile? Document { get; set; }
}

public sealed class SupplierOfferExistingDocumentViewModel
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public int? BidItemId { get; set; }
    public int? BidGuaranteeId { get; set; }
    public long SizeBytes { get; set; }
}
