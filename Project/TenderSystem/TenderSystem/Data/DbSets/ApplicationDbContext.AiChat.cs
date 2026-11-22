using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet properties for the saved chats of the full-page assistant (AI-17) only.
public partial class ApplicationDbContext
{
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
}
