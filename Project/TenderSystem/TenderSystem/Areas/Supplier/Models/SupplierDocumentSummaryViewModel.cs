namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierDocumentSummaryViewModel
{
    public int Id { get; init; }
    public string DocType { get; init; } = string.Empty;
    public DateTime? ExpiryDate { get; init; }
    public DateTime UploadedAt { get; init; }
    public string FileExtension { get; init; } = string.Empty;
}
