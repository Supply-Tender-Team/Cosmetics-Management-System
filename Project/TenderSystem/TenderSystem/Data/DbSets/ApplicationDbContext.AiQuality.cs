using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet for the Admin's test questions of the assistant (AI-20) only.
public partial class ApplicationDbContext
{
    public DbSet<GoldenQuestion> GoldenQuestions => Set<GoldenQuestion>();
}
