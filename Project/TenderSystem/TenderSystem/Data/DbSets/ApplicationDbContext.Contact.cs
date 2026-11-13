using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Contact;

namespace TenderSystem.Data;

public partial class ApplicationDbContext
{
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
}
