using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Ai;

/// <summary>
/// One company document the full-page assistant may answer from (AI-18). Only its text is kept, cut into <see cref="KnowledgeChunk"/>s;
/// the uploaded file itself is not stored. An Admin adds it and chooses which roles may get answers from it.
/// </summary>
// OWNER: Wadea  |  table: KnowledgeDocuments
public class KnowledgeDocument
{
    public int Id { get; set; }

    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(260)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>Roles that may get answers from this document, written as ",Admin,Requester," (a comma before and after every role).</summary>
    [StringLength(300)]
    public string AllowedRoles { get; set; } = ",";

    /// <summary>False = kept but not used for answers.</summary>
    public bool IsEnabled { get; set; } = true;

    public int ChunkCount { get; set; }

    public int CharCount { get; set; }

    /// <summary>The embedding model that made the vectors of the chunks. A question can only be compared with vectors of the same model.</summary>
    [StringLength(100)]
    public string EmbeddingModel { get; set; } = string.Empty;

    public string UploadedById { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser UploadedBy { get; set; } = null!;
    public List<KnowledgeChunk> Chunks { get; set; } = [];
}
