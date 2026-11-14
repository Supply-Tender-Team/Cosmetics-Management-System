namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierProfilePageViewModel
{
    public SupplierProfileEditViewModel Profile { get; set; } = new();
    public SupplierDocumentUploadViewModel DocumentUpload { get; set; } = new();
    public string SupplierStatus { get; init; } = string.Empty;
    public IReadOnlyList<SupplierDocumentSummaryViewModel> Documents { get; init; } = [];
}
