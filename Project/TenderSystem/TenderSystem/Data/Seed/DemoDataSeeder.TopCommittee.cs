using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Infrastructure;

namespace TenderSystem.Data.Seed;

public static partial class DemoDataSeeder
{
    // The numbers the demo seeders give to their tenders. Tenders made by people through the screens are never touched.
    private static readonly string[] DemoTenderPrefixes = ["DEMO-", "LAB-", "TND-DEMO-", "TND-EXP-", "TND-PEND-", "TND-TEST-"];

    /// <summary>
    /// Every DEMO tender goes to the Top Committee after the Manager approves (so a demo always shows the whole route). Older databases have demo tenders with
    /// the Top Committee switch off (the "direct award" route): this switches it on for the ones that are not finished yet. A tender that is already
    /// awarded, rejected or cancelled is never changed, and neither is a tender somebody created through the screens (only the numbers of the demo seeders count).
    /// It runs only in Development, does nothing when there is nothing to change, and never stops the application from starting.
    /// </summary>
    public static async Task FixDemoTopCommitteeAsync(IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment) return;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");
        try
        {
            string[] notFinished =
            [
                Statuses.TenderStatus.Draft, Statuses.TenderStatus.Published, Statuses.TenderStatus.Open, Statuses.TenderStatus.Closed,
                Statuses.TenderStatus.UnderEvaluation, Statuses.TenderStatus.AwaitingManagerReview
            ];
            var changed = await db.Tenders
                .Where(t => !t.RequiresTopCommittee && notFinished.Contains(t.Status)
                    && DemoTenderPrefixes.Any(prefix => t.TenderNumber.StartsWith(prefix)))
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RequiresTopCommittee, true));
            if (changed == 0) return;

            db.AuditLogs.Add(new AuditLog
            {
                Action = "DemoDataFixed", EntityName = "Tender", EntityId = "demo-top-committee",
                NewValues = $"Demo seeds: the Top Committee switch was turned on for {changed} unfinished demo tender(s), so every demo tender goes to the Top Committee after the Manager."
            });
            await db.SaveChangesAsync();
            log.LogInformation("Demo data: {Count} demo tender(s) now go to the Top Committee.", changed);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "The Top Committee switch of the demo tenders could not be updated.");
        }
    }
}
