using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Evaluation;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet for the AI tender summaries (AI-06) only.
public partial class ApplicationDbContext
{
    public DbSet<AiTenderSummary> AiTenderSummaries => Set<AiTenderSummary>();
}
