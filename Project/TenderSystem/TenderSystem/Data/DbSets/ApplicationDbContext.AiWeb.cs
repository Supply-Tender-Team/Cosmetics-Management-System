using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet for the websites allowed in the assistant's web mode (AI-19) only.
public partial class ApplicationDbContext
{
    public DbSet<WebAllowedDomain> WebAllowedDomains => Set<WebAllowedDomain>();
}
