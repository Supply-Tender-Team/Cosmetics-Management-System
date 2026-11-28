using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Settings of the AI tender summary (AI-06). It uses the same model as the bid analysis (AI_ANALYSIS_PROVIDER, OLLAMA_*).
///   AI_SUMMARY_TIMEOUT_SECONDS   time allowed for ONE summary of a whole tender (default 300; a local model on a CPU can be slow)
/// </summary>
public class AiSummaryOptions
{
    public int TimeoutSeconds { get; set; } = 300;
}

public class AiSummaryModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiSummaryOptions>(o =>
        {
            o.TimeoutSeconds = Env.GetInt("AI_SUMMARY_TIMEOUT_SECONDS", o.TimeoutSeconds);
        });
        services.AddScoped<AiTenderSummaryService>();
    }
}
