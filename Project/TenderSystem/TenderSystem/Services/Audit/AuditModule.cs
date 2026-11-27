using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Audit;

public class AuditModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Dependency injection supplies request context and one AuditService per scope.
        services.AddHttpContextAccessor();
        services.AddScoped<AuditService>();
    }
}
