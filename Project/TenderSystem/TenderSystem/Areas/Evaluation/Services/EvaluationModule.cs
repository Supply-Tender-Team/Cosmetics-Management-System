using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Services;

public class EvaluationModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EvaluationReportService>();
        services.AddScoped<CommitteeRecommendationService>();
    }
}
