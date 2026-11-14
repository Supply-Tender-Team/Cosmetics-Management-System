using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>What the browser sends for earlier turns of the chat.</summary>
public sealed record AiChatTurn(string Role, string Text);

/// <summary>Status is the HTTP status the controller should answer with.</summary>
public sealed record AiChatResult(bool Ok, string Text, int Status = 200);

/// <summary>
/// Answers the floating chat. Checks the question, keeps each user under the per-minute limit, adds the role
/// instructions on the server, and asks whichever provider the .env selects (local Ollama or a hosted API).
/// </summary>
public sealed class AiChatService
{
    // userId -> times of their latest questions (the service lives one request, so the limiter is static)
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Recent = new();

    private readonly AiChatOptions _options;
    private readonly IEnumerable<IAiChatProvider> _providers;
    private readonly ILogger<AiChatService> _log;

    public AiChatService(IOptions<AiChatOptions> options, IEnumerable<IAiChatProvider> providers, ILogger<AiChatService> log)
    {
        _options = options.Value;
        _providers = providers;
        _log = log;
    }

    public bool Enabled => _options.Provider is "ollama" or "openai";

    public async Task<AiChatResult> AskAsync(ClaimsPrincipal user, string userId, string? message, IEnumerable<AiChatTurn>? history, string? page, CancellationToken cancellationToken)
    {
        if (!Enabled)
            return new AiChatResult(false, "The AI assistant is turned off.", 503);

        message = message?.Trim();
        if (string.IsNullOrEmpty(message))
            return new AiChatResult(false, "Please type a question.", 400);
        if (message.Length > _options.MaxMessageChars)
            return new AiChatResult(false, $"Please keep your question under {_options.MaxMessageChars} characters.", 400);

        if (!UnderLimit(userId))
            return new AiChatResult(false, "You are asking too fast. Please wait a moment and try again.", 429);

        var role = AiChatPrompts.RoleOf(user);
        var messages = new List<AiChatMessage> { new("system", AiChatPrompts.BuildSystemPrompt(role, AiChatPrompts.CleanPage(page))) };

        // The browser keeps the conversation, so treat it as untrusted: only user/assistant turns, limited in number and size.
        foreach (var turn in (history ?? []).Where(t => t.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(t.Text))
                                            .TakeLast(_options.MaxHistoryMessages))
            messages.Add(new AiChatMessage(turn.Role, turn.Text.Length > 4000 ? turn.Text[..4000] : turn.Text));
        messages.Add(new AiChatMessage("user", message));

        var provider = _providers.First(p => (_options.Provider == "ollama") == (p is OllamaChatProvider));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        try
        {
            var answer = await provider.CompleteAsync(messages, timeout.Token);
            return string.IsNullOrWhiteSpace(answer)
                ? new AiChatResult(false, "The assistant did not give an answer. Please rephrase your question.", 502)
                : new AiChatResult(true, answer);
        }
        catch (AiUnavailableException ex)
        {
            _log.LogWarning(ex, "AI chat provider unavailable");
            return new AiChatResult(false, ex.Message, 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiChatResult(false, "The assistant took too long to answer. Please try a shorter question or try again.", 504);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "AI chat failed");   // the question itself is never logged
            return new AiChatResult(false, "Something went wrong with the assistant. Please try again.", 502);
        }
    }

    private bool UnderLimit(string userId)
    {
        var queue = Recent.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (queue)
        {
            var now = DateTime.UtcNow;
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(1)) queue.Dequeue();
            if (queue.Count >= _options.MaxQuestionsPerMinute) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
