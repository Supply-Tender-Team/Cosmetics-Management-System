using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys and indexes of the knowledge base (AI-18)
public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocuments");
        builder.HasIndex(x => x.UploadedAt);
        // Every foreign key is "Restrict" (see ApplicationDbContext), so KnowledgeBaseService deletes the chunks of a document first.
        builder.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById);
    }
}
