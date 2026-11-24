using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Governance;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet properties for the "Governance" tables only (contracts, verification, evaluation deadline).
public partial class ApplicationDbContext
{
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<DocumentVerification> DocumentVerifications => Set<DocumentVerification>();
    public DbSet<EvaluationSchedule> EvaluationSchedules => Set<EvaluationSchedule>();
}
