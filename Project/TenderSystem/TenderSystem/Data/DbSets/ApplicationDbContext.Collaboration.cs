using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Collaboration;

namespace TenderSystem.Data;

// OWNER: Dev 7  -  DbSet properties for the "Collaboration" tables only.
public partial class ApplicationDbContext
{
    public DbSet<Clarification> Clarifications => Set<Clarification>();
    public DbSet<ClarificationMessage> ClarificationMessages => Set<ClarificationMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DocumentAccessLog> DocumentAccessLogs => Set<DocumentAccessLog>();
}
