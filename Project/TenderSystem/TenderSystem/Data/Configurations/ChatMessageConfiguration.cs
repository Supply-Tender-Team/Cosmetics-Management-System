using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys, indexes and delete rule of the messages of a saved chat (AI-17)
public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");

        builder.HasIndex(x => new { x.SessionId, x.Id });

        builder.HasOne(x => x.Session).WithMany(s => s.Messages).HasForeignKey(x => x.SessionId);
    }
}
