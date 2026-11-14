using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data;

// OWNER: Wadea (DB-04)  -  DbSet properties for the tender requirements and their reviews.
// Own file, so the Tenders and Bids DbSet files of their owners stay untouched.
public partial class ApplicationDbContext
{
    public DbSet<TenderRequirement> TenderRequirements => Set<TenderRequirement>();
    public DbSet<RequirementReview> RequirementReviews => Set<RequirementReview>();
}
