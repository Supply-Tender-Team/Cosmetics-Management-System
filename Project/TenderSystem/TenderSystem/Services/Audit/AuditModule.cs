using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Audit;

public class AuditModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<AuditService>();
    }
}
