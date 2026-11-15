namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierProfileDocumentViewModel
{
    public int Id { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public DateTime? ExpiryDate { get; init; }
    public DateTime UploadedAt { get; init; }
}
