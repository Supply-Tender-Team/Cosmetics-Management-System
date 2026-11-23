using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (helping Dana with TOP-02). Found automatically by AddFeatureModules, so Program.cs is not edited.
public class CommitteeVotingModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Award and rejection emails: read the saved facts, prepare each email, queue it in Wadea's email outbox.
        services.AddScoped<AwardFactsService>();
        services.AddScoped<AwardEmailPreparationService>();
        services.AddScoped<AwardEmailQueueService>();
        services.AddScoped<CommitteeDecisionService>();
        services.AddScoped<CommitteeNotifier>();
        services.AddScoped<CommitteeVotingService>();
    }
}
