using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet properties for the knowledge base of the assistant (AI-18) only.
public partial class ApplicationDbContext
{
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
}
