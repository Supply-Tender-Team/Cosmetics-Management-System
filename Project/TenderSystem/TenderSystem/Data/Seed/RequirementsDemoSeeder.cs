using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;

namespace TenderSystem.Data.Seed;

/// <summary>
/// Demo requirements for the two demo tenders (DEMO-TND-001, DEMO-TND-002) and demo reviews for the offers of the tender that is
/// already under evaluation. Runs AFTER DemoDataSeeder, Development only, only when SEED_DEMO_DATA=true, and only once.
/// It is a separate file so DemoDataSeeder (Marwa) stays untouched.
/// </summary>
public static class RequirementsDemoSeeder
{
    public static async Task SeedAsync(IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment || !Env.GetBool("SEED_DEMO_DATA", false)) return;

        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await db.TenderRequirements.AnyAsync()) return;

        var tenders = await db.Tenders.Where(t => t.TenderNumber == "DEMO-TND-001" || t.TenderNumber == "DEMO-TND-002").ToListAsync();
        var officer = await users.FindByEmailAsync("officer@lumiere.local");
        if (tenders.Count == 0 || officer == null) return;       // the demo data is not there, nothing to attach to

        var now = DateTime.UtcNow;
        foreach (var tender in tenders)
        {
            var locked = tender.Status != Statuses.TenderStatus.Draft;      // published tenders cannot change their requirements
            var order = 0;
            TenderRequirement Add(string name, string description, string type, string? documentType = null) =>
                new()
                {
                    TenderId = tender.Id, Name = name, Description = description, Type = type, DocumentType = documentType,
                    IsMandatory = true, SortOrder = ++order, IsLocked = locked, CreatedAt = now.AddDays(-8)
                };

            db.TenderRequirements.AddRange(
                Add("Tax certificate", "A valid tax certificate must be attached to the offer.", Statuses.RequirementType.Document, "Tax certificate"),
                Add("Company registration", "A valid company registration certificate must be attached.", Statuses.RequirementType.Document, "Commercial registration"),
                Add("Delivery period", "Delivery to the company warehouse within 14 days of the order.", Statuses.RequirementType.Delivery),
                Add("Offer validity", "The offer must stay valid for at least 60 days.", Statuses.RequirementType.Validity),
                Add("Glass bottles", "Clear glass bottles with a capacity of 100 ml.", Statuses.RequirementType.Technical),
                Add("Pump caps", "Leak-resistant pump caps that fit the bottles.", Statuses.RequirementType.Technical));
        }
        await db.SaveChangesAsync();

        // The demo offers of the tender under evaluation were already found Compliant by DemoDataSeeder, so the detailed
        // reviews agree with that decision. (The Compliance screen will let the Officer record real ones.)
        var evaluating = tenders.FirstOrDefault(t => t.Status == Statuses.TenderStatus.UnderEvaluation);
        if (evaluating == null) return;

        var requirements = await db.TenderRequirements.Where(r => r.TenderId == evaluating.Id).ToListAsync();
        var bids = await db.Bids.Where(b => b.TenderId == evaluating.Id && b.Status != Statuses.BidStatus.Draft).ToListAsync();
        foreach (var bid in bids)
            foreach (var requirement in requirements)
                db.RequirementReviews.Add(new RequirementReview
                {
                    BidId = bid.Id, RequirementId = requirement.Id, ReviewedByUserId = officer.Id,
                    Result = Statuses.RequirementResult.Met, Notes = "Demo review, agrees with the demo compliance decision.",
                    ReviewedAt = now.AddHours(-12),
                });
        await db.SaveChangesAsync();
    }
}
