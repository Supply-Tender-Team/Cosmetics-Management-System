using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Settings of ONE named AI profile (AI-15). A profile can have its own model and its own API key, separate from the chat.
///   {PREFIX}_API_KEY, {PREFIX}_API_MODEL, {PREFIX}_API_BASE_URL   a hosted API (any OpenAI "chat/completions" service, for example NVIDIA)
///   {PREFIX}_PROVIDER=ollama and {PREFIX}_OLLAMA_MODEL            or a local model with its own name
///   {PREFIX}_TIMEOUT_SECONDS                                      time allowed for one answer
/// When nothing is set the profile is not "dedicated" and the callers keep using the shared AI settings.
/// </summary>
public sealed class AiProfileOptions
{
    /// <summary>"openai" (hosted API), "ollama" (local model) or "" (not dedicated).</summary>
    public string Provider { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;

    public bool IsDedicated => Provider switch
    {
        "openai" => ApiKey.Length > 0 && Model.Length > 0,
        "ollama" => Model.Length > 0,
        _ => false,
    };
}

public sealed class AiGenerationOptions
{
    /// <summary>AI_INSIGHTS_*: the analysis and summary cards (offer analysis, tender summary, and anything built on them).</summary>
    public AiProfileOptions Insights { get; set; } = new();
    /// <summary>AI_EMAIL_*: award and other emails written by the AI.</summary>
    public AiProfileOptions Email { get; set; } = new();
}

/// <summary>Registers the shared generation service. Found automatically by Program.cs (IFeatureModule).</summary>
public class AiGenerationModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiGenerationOptions>(o =>
        {
            o.Insights = Read("AI_INSIGHTS", 180);
            o.Email = Read("AI_EMAIL", 60);
        });
        services.Configure<AiReportOptions>(o => o.TimeoutSeconds = Env.GetInt("AI_REPORT_TIMEOUT_SECONDS", o.TimeoutSeconds));
        services.AddScoped<AiGenerationService>();
        services.AddScoped<AiAwardEmailService>();
        services.AddScoped<AiReportService>();
    }

    private static AiProfileOptions Read(string prefix, int defaultTimeoutSeconds)
    {
        var key = Env.GetString(prefix + "_API_KEY", "").Trim();
        var provider = Env.GetString(prefix + "_PROVIDER", key.Length > 0 ? "openai" : "").Trim().ToLowerInvariant();
        return new AiProfileOptions
        {
            Provider = provider,
            ApiKey = key,
            ApiBaseUrl = Env.GetString(prefix + "_API_BASE_URL", Env.GetString("AI_API_BASE_URL", "https://api.openai.com/v1")).Trim().TrimEnd('/'),
            Model = (provider == "ollama" ? Env.GetString(prefix + "_OLLAMA_MODEL", "") : Env.GetString(prefix + "_API_MODEL", "")).Trim(),
            TimeoutSeconds = Env.GetInt(prefix + "_TIMEOUT_SECONDS", defaultTimeoutSeconds),
        };
    }
}
