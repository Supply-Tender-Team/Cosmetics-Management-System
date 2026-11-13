using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Evaluation;

namespace TenderSystem.Data;

// OWNER: Marwa  -  DbSet properties for the "Evaluation" tables only.
public partial class ApplicationDbContext
{
    public DbSet<TechnicalEvaluation> TechnicalEvaluations => Set<TechnicalEvaluation>();
    public DbSet<FinancialEvaluation> FinancialEvaluations => Set<FinancialEvaluation>();
    public DbSet<AiBidAnalysis> AiBidAnalyses => Set<AiBidAnalysis>();
    public DbSet<EvaluationReport> EvaluationReports => Set<EvaluationReport>();
    public DbSet<TenderApproval> TenderApprovals => Set<TenderApproval>();
    public DbSet<TopCommitteeVote> TopCommitteeVotes => Set<TopCommitteeVote>();
}
