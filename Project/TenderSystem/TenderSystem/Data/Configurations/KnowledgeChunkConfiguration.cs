using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys and indexes of the pieces of a knowledge document (AI-18)
public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("KnowledgeChunks");
        builder.HasIndex(x => new { x.DocumentId, x.Position });
        builder.HasOne(x => x.Document).WithMany(d => d.Chunks).HasForeignKey(x => x.DocumentId);
    }
}
