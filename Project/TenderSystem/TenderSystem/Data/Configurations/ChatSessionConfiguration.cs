using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys, indexes and delete rule of the saved chats (AI-17)
public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("ChatSessions");

        // The side list shows "my chats, newest first"; the clean-up job looks for old ones.
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
        builder.HasIndex(x => x.UpdatedAt);

        // Every foreign key is "Restrict" (see ApplicationDbContext), so AiChatPageService deletes the messages of a chat before the chat.
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}
