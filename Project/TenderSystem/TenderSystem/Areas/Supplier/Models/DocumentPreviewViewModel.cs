namespace TenderSystem.Areas.Supplier.Models;

public sealed class DocumentPreviewViewModel
{
    public string Title { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ViewerName { get; init; } = string.Empty;
    public DateTime ViewedAtUtc { get; init; }
    public string FileUrl { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public bool CanPreview { get; init; }
    public bool IsImage { get; init; }
}
