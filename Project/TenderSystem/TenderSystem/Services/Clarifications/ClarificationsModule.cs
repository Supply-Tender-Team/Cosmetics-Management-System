using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Clarifications;

public sealed class ClarificationsModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ClarificationService>();
    }
}
