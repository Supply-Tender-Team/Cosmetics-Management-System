using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;

namespace TenderSystem.Data;

// OWNER: Sara  -  DbSet properties for the "Bids" tables only.
public partial class ApplicationDbContext
{
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<BidItem> BidItems => Set<BidItem>();
    public DbSet<BidDocument> BidDocuments => Set<BidDocument>();
    public DbSet<ComplianceCheck> ComplianceChecks => Set<ComplianceCheck>();
}
