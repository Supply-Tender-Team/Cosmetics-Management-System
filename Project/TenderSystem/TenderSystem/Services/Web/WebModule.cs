using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Web;

/// <summary>
/// Settings of the assistant's web mode (AI-19), from .env. Web mode is OFF until AI_WEB_ENABLED=true and a search key is set.
///   AI_WEB_ENABLED=false             turns web mode on for the whole system
///   AI_WEB_SEARCH_API_KEY=           key of the search service (Brave Search API). A secret: only in your own .env
///   AI_WEB_SEARCH_BASE_URL=          default https://api.search.brave.com/res/v1/web/search
///   AI_WEB_MAX_RESULTS=5             search results looked at for one question
///   AI_WEB_MAX_PAGES=3               pages opened for one question
///   AI_WEB_PAGE_MAX_KB=1024          a page bigger than this is cut
///   AI_WEB_TIMEOUT_SECONDS=10        time allowed for one search or one page
///   AI_WEB_MAX_PER_MINUTE=3          web questions allowed per user per minute
/// </summary>
public class WebOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.search.brave.com/res/v1/web/search";
    public int MaxResults { get; set; } = 5;
    public int MaxPages { get; set; } = 3;
    public int PageMaxKb { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxPerMinute { get; set; } = 3;

    /// <summary>Only for automatic tests (never read from .env): allow plain http and addresses inside the company network.</summary>
    public bool AllowPrivateForTests { get; set; }

    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Registers web mode. Found automatically by Program.cs (IFeatureModule).</summary>
public class WebModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebOptions>(o =>
        {
            o.Enabled = Env.GetBool("AI_WEB_ENABLED", o.Enabled);
            o.ApiKey = Env.GetString("AI_WEB_SEARCH_API_KEY", o.ApiKey);
            o.BaseUrl = Env.GetString("AI_WEB_SEARCH_BASE_URL", o.BaseUrl);
            o.MaxResults = Math.Clamp(Env.GetInt("AI_WEB_MAX_RESULTS", o.MaxResults), 1, 10);
            o.MaxPages = Math.Clamp(Env.GetInt("AI_WEB_MAX_PAGES", o.MaxPages), 1, 5);
            o.PageMaxKb = Math.Clamp(Env.GetInt("AI_WEB_PAGE_MAX_KB", o.PageMaxKb), 64, 4096);
            o.TimeoutSeconds = Math.Clamp(Env.GetInt("AI_WEB_TIMEOUT_SECONDS", o.TimeoutSeconds), 3, 60);
            o.MaxPerMinute = Math.Clamp(Env.GetInt("AI_WEB_MAX_PER_MINUTE", o.MaxPerMinute), 1, 30);
        });
        services.AddScoped<WebDomainService>();
        services.AddScoped<SafeWebFetcher>();
        services.AddScoped<WebSearchClient>();
        services.AddScoped<WebAnswerService>();
    }
}
