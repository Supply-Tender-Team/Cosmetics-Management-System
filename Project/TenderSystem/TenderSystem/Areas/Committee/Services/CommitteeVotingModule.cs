using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (helping Dana with TOP-02). Found automatically by AddFeatureModules, so Program.cs is not edited.
public class CommitteeVotingModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Read-only helpers for the future Awarded emails. Registered only; nothing calls them yet.
        services.AddScoped<AwardFactsService>();
        services.AddScoped<AwardEmailPreparationService>();
        services.AddScoped<CommitteeDecisionService>();
        services.AddScoped<CommitteeNotifier>();
        services.AddScoped<CommitteeVotingService>();
    }
}
