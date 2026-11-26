using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  unique key and index of AiSavedResults
public class AiSavedResultConfiguration : IEntityTypeConfiguration<AiSavedResult>
{
    public void Configure(EntityTypeBuilder<AiSavedResult> builder)
    {
        // One saved result per user, kind and key: a new result replaces the old one.
        builder.HasIndex(x => new { x.Kind, x.OwnerUserId, x.Key }).IsUnique();
        builder.HasIndex(x => x.Token);
    }
}
