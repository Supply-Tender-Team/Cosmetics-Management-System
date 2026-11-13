using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data;

/// <summary>
/// The ONE database context.  Do NOT add DbSet properties here.
///   - Add your DbSet in YOUR partial file:  Data/DbSets/ApplicationDbContext.&lt;Feature&gt;.cs
///   - Add your table mapping in YOUR file:  Data/Configurations/&lt;Entity&gt;Configuration.cs
///     (a class implementing IEntityTypeConfiguration&lt;T&gt;, picked up automatically below)
/// This way nobody edits the same file and there are no merge conflicts.
/// </summary>
public partial class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // SQL Server rejects tables with several cascade paths, and we never want a delete to silently
        // wipe related rows (offers, decisions, audit data). So every foreign key of OUR tables is "Restrict":
        // the database refuses to delete a row that is still used. (Identity's own tables keep their defaults.)
        foreach (var foreignKey in builder.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Namespace?.StartsWith("TenderSystem.Data.Entities") == true)
                     .SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
