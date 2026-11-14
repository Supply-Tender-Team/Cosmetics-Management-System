using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier;

public sealed class SupplierFeatureModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddHostedService<SupplierDemoDataSeeder>();
}
