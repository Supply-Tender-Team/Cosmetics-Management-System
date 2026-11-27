using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data;

// OWNER: Wadea  -  DbSet property for the saved AI results (AI-24).
public partial class ApplicationDbContext
{
    public DbSet<AiSavedResult> AiSavedResults => Set<AiSavedResult>();
}
