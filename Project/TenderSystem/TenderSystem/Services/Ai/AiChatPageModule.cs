using DotNetEnv;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Settings of the full-page assistant (AI-17), from .env:
///   AI_CHAT_HISTORY_DAYS      days a chat is kept after its last message (default 30, minimum 1)
///   AI_CHAT_MAX_MESSAGES      messages allowed in one chat (default 200)
///   AI_CHAT_CLEANUP_MINUTES   how often the clean-up job looks for old chats (default 360)
/// </summary>
public class AiChatPageOptions
{
    public int HistoryDays { get; set; } = 30;
    public int MaxMessagesPerChat { get; set; } = 200;
    public int CleanupMinutes { get; set; } = 360;
}

/// <summary>Registers the full-page assistant. Found automatically by Program.cs (IFeatureModule).</summary>
public class AiChatPageModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiChatPageOptions>(o =>
        {
            o.HistoryDays = Math.Max(1, Env.GetInt("AI_CHAT_HISTORY_DAYS", o.HistoryDays));
            o.MaxMessagesPerChat = Math.Clamp(Env.GetInt("AI_CHAT_MAX_MESSAGES", o.MaxMessagesPerChat), 2, 2000);
            o.CleanupMinutes = Math.Clamp(Env.GetInt("AI_CHAT_CLEANUP_MINUTES", o.CleanupMinutes), 5, 10080);
        });
        services.AddScoped<AiChatPageService>();
        services.AddHostedService<AiChatCleanupWorker>();
    }
}

/// <summary>Deletes saved chats that are older than AI_CHAT_HISTORY_DAYS. Runs a few minutes after start and then every AI_CHAT_CLEANUP_MINUTES.</summary>
public sealed class AiChatCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AiChatPageOptions _options;
    private readonly ILogger<AiChatCleanupWorker> _log;

    public AiChatCleanupWorker(IServiceScopeFactory scopes, IOptions<AiChatPageOptions> options, ILogger<AiChatCleanupWorker> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch (OperationCanceledException) { return; }   // let the database setup finish first

        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var removed = await scope.ServiceProvider.GetRequiredService<AiChatPageService>().DeleteOldAsync(stop);
                if (removed > 0) _log.LogInformation("Removed {Count} saved chat(s) older than {Days} days.", removed, _options.HistoryDays);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Chat clean-up failed. Is the AiChatPage migration applied? Trying again later.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(_options.CleanupMinutes), stop); } catch (OperationCanceledException) { break; }
        }
    }
}
