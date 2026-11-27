using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>Registers the assistant chat. Found automatically by Program.cs (IFeatureModule).</summary>
public class AiModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // The service applies its own timeout (AI_CHAT_TIMEOUT_SECONDS), so the client itself never cuts a slow local model.
        services.AddHttpClient("ai", c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<IAiChatProvider, OllamaChatProvider>();
        services.AddScoped<IAiChatProvider, OpenAiChatProvider>();
        services.AddScoped<RoleDataService>();
        services.AddScoped<QuestionService>();
        services.AddScoped<AiNudgeService>();
        services.AddMemoryCache();
    }
}
