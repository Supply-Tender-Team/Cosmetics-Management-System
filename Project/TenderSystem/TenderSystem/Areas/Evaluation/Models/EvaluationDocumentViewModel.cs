namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationDocumentViewModel
{
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsImage => ContentType is "image/jpeg" or "image/png" or "image/webp" or "image/gif";
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
}
