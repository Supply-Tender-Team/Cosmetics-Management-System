using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    /// <summary>
    /// The Sourcing Manager only REVIEWS the finished evaluation report: it never sits on an evaluation committee and never scores.
    /// Older demo data (and older databases of the team) put the demo manager accounts on the 4th committee seat. This moves those seats to
    /// an Evaluation Committee demo account and removes the Evaluation Committee role from every Sourcing Manager account, by running
    /// Data/Seed/fix-manager-evaluation-seats.sql. It does nothing when there is nothing to fix, never runs outside Development, and
    /// never stops the application from starting.
    /// </summary>
    public static async Task FixManagerSeatsAsync(IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment) return;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");
        try
        {
            var managerIds = from userRole in db.UserRoles
                             join role in db.Roles on userRole.RoleId equals role.Id
                             where role.Name == Roles.SourcingManager
                             select userRole.UserId;
            var hasSeat = await db.CommitteeMembers.AnyAsync(member => managerIds.Contains(member.UserId));
            var hasEvaluationRole = await (from userRole in db.UserRoles
                                           join role in db.Roles on userRole.RoleId equals role.Id
                                           where role.Name == Roles.EvaluationCommittee && managerIds.Contains(userRole.UserId)
                                           select userRole.UserId).AnyAsync();
            if (!hasSeat && !hasEvaluationRole) return;

            var path = Path.Combine(services.GetRequiredService<IHostEnvironment>().ContentRootPath, "Data", "Seed", "fix-manager-evaluation-seats.sql");
            if (!File.Exists(path))
            {
                log.LogWarning("Sourcing Manager accounts still sit on evaluation committees, but {Path} was not found.", path);
                return;
            }
#pragma warning disable EF1002 // the text is our own script file, not user input
            await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(path));
#pragma warning restore EF1002
            log.LogInformation("Demo data: Sourcing Manager accounts no longer sit on evaluation committees.");
        }
        catch (Exception ex)
        {
            // Demo data must never stop the application. Run the script by hand: sqlcmd -S localhost -d <database> -E -C -I -i Data\Seed\fix-manager-evaluation-seats.sql
            log.LogWarning(ex, "The Sourcing Manager seats could not be moved to an Evaluation Committee account.");
        }
    }
}
