using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data;

// OWNER: Dev 4  -  DbSet properties for the "Tenders" tables only.
public partial class ApplicationDbContext
{
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderSupplier> TenderSuppliers => Set<TenderSupplier>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<EvaluationCriterion> EvaluationCriteria => Set<EvaluationCriterion>();
    public DbSet<CommitteeMember> CommitteeMembers => Set<CommitteeMember>();
}
