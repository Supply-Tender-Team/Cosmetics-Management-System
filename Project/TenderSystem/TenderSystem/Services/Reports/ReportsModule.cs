using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public class ReportsModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ReportQueryService>();
        services.AddScoped<ReportExcelWriter>();
    }
}
