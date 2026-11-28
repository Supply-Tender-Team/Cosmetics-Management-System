using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Settings of the bid analysis (AI-01). Own class and own module, so nobody has to edit shared files.
///   AI_ANALYSIS_PROVIDER=ollama   local model (default; uses OLLAMA_BASE_URL / OLLAMA_MODEL)
///   AI_ANALYSIS_PROVIDER=openai   hosted API (uses AI_API_*). Offer prices and documents then leave the company: decide that first.
///   AI_ANALYSIS_PROVIDER=off      the analysis is not available
///   AI_ANALYSIS_TIMEOUT_SECONDS   time allowed for ONE offer (default 180)
/// </summary>
public class AiAnalysisOptions
{
    public string Provider { get; set; } = "ollama";
    public int TimeoutSeconds { get; set; } = 180;
}

public class AiAnalysisModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiAnalysisOptions>(o =>
        {
            o.Provider = Env.GetString("AI_ANALYSIS_PROVIDER", o.Provider).Trim().ToLowerInvariant();
            o.TimeoutSeconds = Env.GetInt("AI_ANALYSIS_TIMEOUT_SECONDS", o.TimeoutSeconds);
        });
        services.AddScoped<AiBidAnalysisService>();
    }
}
