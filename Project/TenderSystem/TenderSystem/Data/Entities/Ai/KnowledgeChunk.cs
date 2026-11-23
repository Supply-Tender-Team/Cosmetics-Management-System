namespace TenderSystem.Data.Entities.Ai;

/// <summary>A piece of a <see cref="KnowledgeDocument"/> (a few hundred words) with its embedding, so the assistant can find it by meaning (AI-18).</summary>
// OWNER: Wadea  |  table: KnowledgeChunks
public class KnowledgeChunk
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    /// <summary>1, 2, 3... the order of the pieces inside the document.</summary>
    public int Position { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>The embedding: float numbers (4 bytes each), length 1, so a dot product is the cosine similarity.</summary>
    public byte[] Embedding { get; set; } = [];

    public KnowledgeDocument Document { get; set; } = null!;
}
