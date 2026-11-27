namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierOffersViewModel
{
    public IReadOnlyList<SupplierOfferDetailRow> Offers { get; init; } = [];
}

public sealed class SupplierOfferDetailRow
{
    public int Id { get; init; }
    public int TenderId { get; init; }
    public string TenderNumber { get; init; } = "";
    public string TenderTitle { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal TotalPrice { get; init; }
    public string Currency { get; init; } = "";
    public DateTime? SubmittedAt { get; init; }
    public bool CanWithdraw { get; init; }
    public IReadOnlyList<SupplierOfferDocumentRow> Documents { get; init; } = [];
}

public sealed class SupplierOfferDocumentRow
{
    public int Id { get; init; }
    public string FileName { get; init; } = "";
    public string DocType { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool IsGuaranteeDocument { get; init; }
}
