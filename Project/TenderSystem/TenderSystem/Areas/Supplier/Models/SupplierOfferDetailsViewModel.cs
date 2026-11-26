namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierOfferDetailsViewModel
{
    public int Id { get; init; }
    public int TenderId { get; init; }
    public string TenderNumber { get; init; } = string.Empty;
    public string TenderTitle { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool? WasSelected { get; init; }
    public decimal TotalPrice { get; init; }
    public string Currency { get; init; } = string.Empty;
    public int DeliveryDays { get; init; }
    public int ValidityDays { get; init; }
    public string PaymentTerms { get; init; } = string.Empty;
    public string TechnicalSummary { get; init; } = string.Empty;
    public DateTime SubmissionDeadline { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public IReadOnlyList<SupplierOfferDetailsItemRow> Items { get; init; } = [];
    public IReadOnlyList<SupplierOfferDetailsDocumentRow> Documents { get; init; } = [];
    public IReadOnlyList<SupplierOfferDetailsGuaranteeRow> Guarantees { get; init; } = [];
}

public sealed class SupplierOfferDetailsItemRow
{
    public int Id { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public decimal TaxRate { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal InstallationCost { get; init; }
    public decimal LineTotal { get; init; }
    public string OfferedSpecifications { get; init; } = string.Empty;
}

public sealed class SupplierOfferDetailsDocumentRow
{
    public int Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string DocType { get; init; } = string.Empty;
    public int? BidItemId { get; init; }
}

public sealed class SupplierOfferDetailsGuaranteeRow
{
    public string GuaranteeType { get; init; } = string.Empty;
    public string BeneficiaryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? ReferenceNumber { get; init; }
    public string? IssuerName { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
