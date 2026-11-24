using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Governance;

/// <summary>Registers the RPT-01 services (evaluation deadline, contracts, verification). Found automatically by Program.cs (IFeatureModule).</summary>
public class GovernanceModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EvaluationScheduleOptions>(o => o.Days = Env.GetInt("EVALUATION_SLA_DAYS", o.Days));
        services.AddScoped<EvaluationScheduleService>();
        services.AddScoped<ContractService>();
        services.AddScoped<VerificationService>();
    }
}
