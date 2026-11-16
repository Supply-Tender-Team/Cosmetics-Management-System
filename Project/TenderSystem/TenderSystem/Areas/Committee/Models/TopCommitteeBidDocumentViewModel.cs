namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeBidDocumentViewModel
{
    public int Id { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
