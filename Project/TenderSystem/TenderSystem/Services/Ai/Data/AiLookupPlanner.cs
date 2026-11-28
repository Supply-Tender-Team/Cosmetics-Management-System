using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Settings of the lookup planner (AI-21), from .env:
///   AI_PLANNER=always      always ask the model which lookup fits (best answers, one extra short model call per question)
///   AI_PLANNER=off         no list lookups (only a record number in the question, the helpers and the small overview)
///   AI_PLANNER_TIMEOUT_SECONDS=25   time allowed for the choice; after that the small overview is used
/// </summary>
public class AiPlannerOptions
{
    public string Mode { get; set; } = "always";
    public int TimeoutSeconds { get; set; } = 25;
}

/// <summary>
/// AI-21: lets the model CHOOSE which read-only lookup answers a question, instead of matching keywords. The model gets the list of lookups its
/// user's role has (<see cref="AiLookupCatalog"/>) and answers with a small JSON object. It never writes a query and never sees data here;
/// the server runs the chosen lookup with the user's own scope (RoleDataService.RunToolAsync). Any problem returns null and the small overview is used (a record number in the question is still read).
/// </summary>
public sealed class AiLookupPlanner
{
    private readonly IEnumerable<IAiChatProvider> _providers;
    private readonly AiChatOptions _chat;
    private readonly AiPlannerOptions _options;
    private readonly ILogger<AiLookupPlanner> _log;

    public AiLookupPlanner(IEnumerable<IAiChatProvider> providers, IOptions<AiChatOptions> chat, IOptions<AiPlannerOptions> options, ILogger<AiLookupPlanner> log)
    {
        _providers = providers;
        _chat = chat.Value;
        _options = options.Value;
        _log = log;
    }

    public bool Enabled => !string.Equals(_options.Mode, "off", StringComparison.OrdinalIgnoreCase) && _chat.Provider is "ollama" or "openai";

    public async Task<AiToolCall?> PlanAsync(string role, bool seat, string question, IReadOnlyList<AiChatTurn> earlier, string? contextLabel, string? contextNumber, CancellationToken ct)
    {
        if (!Enabled) return null;
        var tools = AiLookupCatalog.For(role, seat);
        if (tools.Count == 0) return null;

        var messages = new List<AiChatMessage> { new("system", BuildPrompt(role, tools, contextLabel, contextNumber)) };
        // the last turns, so a short follow-up ("and the delivery?") can be understood
        foreach (var turn in earlier.TakeLast(2))
            messages.Add(new AiChatMessage(turn.Role, turn.Text.Length > 300 ? turn.Text[..300] : turn.Text));
        messages.Add(new AiChatMessage("user", question.Length > 600 ? question[..600] : question));

        var provider = _providers.First(p => (_chat.Provider == "ollama") == (p is OllamaChatProvider));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        try
        {
            var answer = await provider.CompleteAsync(messages, timeout.Token, json: true);
            var call = Parse(answer, tools);
            if (call is null) _log.LogInformation("Lookup planner gave no usable choice");
            return call;
        }
        catch (Exception ex) when (ex is AiUnavailableException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogInformation("Lookup planner unavailable ({Type}); using the overview", ex.GetType().Name);
            return null;
        }
    }

    public static string BuildPrompt(string role, IReadOnlyList<AiToolDef> tools, string? contextLabel, string? contextNumber)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You choose which read-only lookup answers the user's latest question in a sourcing and tender system. The user's role is {Roles.LabelFor(role)}.");
        sb.AppendLine("Reply with ONE JSON object and nothing else: {\"tool\":\"<name>\",\"args\":{...}}. All argument values are text. Leave out arguments you do not need.");
        sb.AppendLine("Special tools: \"record\" = the question is about ONE specific request or tender, read as it is (its details, its status, its offers, its terms): put its number in args.number. Use the help tool, not record, when the user wants something WRITTEN, CHECKED or COMPARED. \"none\" = a how-to or general question that needs no data.");
        sb.AppendLine("Lookups you may choose (pick the one whose filters match the question; use counts for \"how many\"):");
        foreach (var t in tools) sb.AppendLine("- " + t.Doc);
        if (!string.IsNullOrEmpty(contextNumber))
            sb.AppendLine($"The user is looking at this record right now: {contextLabel}. When they say \"this\", \"here\" or \"it\" without a number, use {contextNumber} (as tender_number or args.number).");
        sb.AppendLine("Examples:");
        sb.AppendLine(AiLookupCatalog.Examples(role).Trim());
        sb.AppendLine("\"How do I create a request?\" -> {\"tool\":\"none\",\"args\":{}}");
        return sb.ToString().Trim();
    }

    /// <summary>The choice in the model's answer, or null when it is not valid JSON or names a lookup this role does not have.</summary>
    public static AiToolCall? Parse(string? answer, IReadOnlyList<AiToolDef> tools)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(answer[start..(end + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("tool", out var toolElement)) return null;
            var tool = (toolElement.GetString() ?? "").Trim().ToLowerInvariant();
            if (tool != AiToolCall.Record && tool != AiToolCall.None && tools.All(t => t.Name != tool)) return null;

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("args", out var argElement) && argElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in argElement.EnumerateObject().Take(12))
                {
                    var value = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(value) && value.Length <= 120) args[p.Name.ToLowerInvariant()] = value.Trim();
                }
            }
            return new AiToolCall(tool, args);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Registers the planner. Found automatically by Program.cs (IFeatureModule).</summary>
public class AiPlannerModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiPlannerOptions>(o =>
        {
            o.Mode = DotNetEnv.Env.GetString("AI_PLANNER", o.Mode).Trim().ToLowerInvariant();
            o.TimeoutSeconds = Math.Clamp(DotNetEnv.Env.GetInt("AI_PLANNER_TIMEOUT_SECONDS", o.TimeoutSeconds), 5, 120);
        });
        services.AddScoped<AiLookupPlanner>();
    }
}
