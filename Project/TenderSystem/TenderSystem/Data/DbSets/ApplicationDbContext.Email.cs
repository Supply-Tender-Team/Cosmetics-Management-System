using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Messaging;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet properties for the "Email" tables only.
public partial class ApplicationDbContext
{
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();
}
