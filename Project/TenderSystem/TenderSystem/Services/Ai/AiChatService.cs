using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Audit;

namespace TenderSystem.Services.Ai;

/// <summary>What the browser sends for earlier turns of the chat.</summary>
/// <param name="Numbers">for an assistant turn that showed a table: the record numbers of its rows (so "the second one" can be understood)</param>
public sealed record AiChatTurn(string Role, string Text, string[]? Numbers = null);

/// <summary>Status is the HTTP status the controller should answer with.</summary>
public sealed record AiChatResult(bool Ok, string Text, int Status = 200, AiTable? Table = null);

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
    private readonly AiDataService _data;
    private readonly AuditService _audit;

    private readonly AiActionOptions _actions;
    private readonly AiLookupPlanner _planner;

    public AiChatService(IOptions<AiChatOptions> options, IEnumerable<IAiChatProvider> providers, ILogger<AiChatService> log,
        AiDataService data, AuditService audit, IOptions<AiActionOptions> actions, AiLookupPlanner planner)
    {
        _planner = planner;
        _actions = actions.Value;
        _options = options.Value;
        _providers = providers;
        _log = log;
        _data = data;
        _audit = audit;
    }

    public bool Enabled => _options.Provider is "ollama" or "openai";

    /// <summary>
    /// Reading the database for the assistant is on by default. With a HOSTED model the data would leave the company,
    /// so it also needs AI_CHAT_DATA_WITH_API=true. With the local Ollama model it never leaves this computer.
    /// </summary>
    private bool DataAllowed => _options.Data && (_options.Provider == "ollama" || _options.DataWithApi);

    /// <summary>Company data (database rows, company documents) may be given to the model: same rule as the database data.</summary>
    public bool CompanyDataAllowed => DataAllowed;

    /// <summary>Everything needed to ask the model: the finished conversation and the table to show under the answer. Error is set when the question must not be asked.</summary>
    public sealed record AiChatPrepared(AiChatResult? Error, List<AiChatMessage> Messages, AiTable? Table, string Question, string? ContextLabel = null);

    public async Task<AiChatResult> AskAsync(ClaimsPrincipal user, string userId, string? message, IEnumerable<AiChatTurn>? history, string? page, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(user, userId, message, history, page, cancellationToken);
        if (prepared.Error != null) return prepared.Error;
        var messages = prepared.Messages;
        var data = prepared.Table;

        var provider = Provider;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        try
        {
            var answer = await provider.CompleteAsync(messages, timeout.Token);
            return string.IsNullOrWhiteSpace(answer)
                ? new AiChatResult(false, "The assistant did not give an answer. Please rephrase your question.", 502)
                : new AiChatResult(true, answer, 200, data);
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

    /// <summary>
    /// AI-21: "Tender TND-001: title" when the page the user came from is about a record they may see, otherwise null. The browser's page address is
    /// only turned into a number and checked with the user's own scope, so it can never reveal a record they may not see.
    /// </summary>
    public async Task<string?> ContextLabelAsync(ClaimsPrincipal user, string userId, string? page, CancellationToken cancellationToken)
    {
        var role = AiChatPrompts.RoleOf(user);
        if (!DataAllowed || role == "") return null;
        var number = await _data.RecordNumberOfPageAsync(page, cancellationToken);
        return number == null ? null : await _data.VisibleRecordLabelAsync(role, userId, number, cancellationToken);
    }

    /// <summary>The provider chosen in .env (AI_CHAT_PROVIDER).</summary>
    public IAiChatProvider Provider => _providers.First(p => (_options.Provider == "ollama") == (p is OllamaChatProvider));

    /// <summary>Seconds without a new piece of the answer after which a streamed answer is given up.</summary>
    public int TimeoutSeconds => Math.Max(5, _options.TimeoutSeconds);

    /// <summary>
    /// Checks the question, keeps the user under the per-minute limit, reads the role-limited data and builds the conversation.
    /// Shared by the floating chat (<see cref="AskAsync"/>) and the full page, so both follow exactly the same rules.
    /// </summary>
    /// <param name="sourcesFor">
    /// Full page only: looks up passages of the company documents for the question and returns them as numbered text. Null = not searched
    /// (the prompt then says nothing about documents), empty = searched and nothing matched. Only called when company data may reach the model.
    /// </param>
    /// <param name="webFor">
    /// Full page, web mode switched on for this question (AI-19): searches the websites the Admin allowed and returns numbered passages
    /// (empty = nothing found). Null = web mode is off. Unlike company data this may be used with a hosted model, because it is public text.
    /// </param>
    public async Task<AiChatPrepared> PrepareAsync(ClaimsPrincipal user, string userId, string? message, IEnumerable<AiChatTurn>? history, string? page,
        CancellationToken cancellationToken, Func<string, CancellationToken, Task<string?>>? sourcesFor = null,
        Func<string, CancellationToken, Task<string?>>? webFor = null)
    {
        static AiChatPrepared Fail(string text, int status) => new(new AiChatResult(false, text, status), [], null, "");

        if (!Enabled)
            return Fail("The AI assistant is turned off.", 503);

        message = message?.Trim();
        if (string.IsNullOrEmpty(message))
            return Fail("Please type a question.", 400);
        if (message.Length > _options.MaxMessageChars)
            return Fail($"Please keep your question under {_options.MaxMessageChars} characters.", 400);

        if (!UnderLimit(userId))
            return Fail("You are asking too fast. Please wait a moment and try again.", 429);

        var role = AiChatPrompts.RoleOf(user);
        var earlier = (history ?? []).Where(t => t.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(t.Text)).TakeLast(_options.MaxHistoryMessages).ToList();
        // AI-11: a follow-up ("what about its delivery?") borrows the record number of the earlier turns, for the lookup only
        var lookupQuestion = AiFollowUp.Resolve(message, earlier);

        // Real data for this question, limited to this user's role INSIDE the queries (see AiDataService). Failing to load it
        // must never break the chat: the assistant then simply answers without data.
        AiDataResult? data = null;
        string? contextLabel = null;
        if (DataAllowed && role != "")
        {
            try
            {
                // AI-21: the request or tender the user is looking at (the page they came from), only when they may see it
                string? contextNumber = null;
                var recordOfPage = await _data.RecordNumberOfPageAsync(page, cancellationToken);
                if (recordOfPage != null)
                {
                    contextLabel = await _data.VisibleRecordLabelAsync(role, userId, recordOfPage, cancellationToken);
                    if (contextLabel != null) contextNumber = recordOfPage;
                }

                // AI-21: the model chooses which lookup fits (with the filters), instead of keyword matching. If it cannot, the keyword path below is used.
                var seat = await _data.HasSeatAsync(role, userId, cancellationToken);
                var plan = await _planner.PlanAsync(role, seat, message, earlier, contextLabel, contextNumber, cancellationToken);
                // "this tender" / "this request": whatever list the model picked, the question is about the record on the screen
                if (plan != null && contextNumber != null && PointsAtThis(message))
                {
                    if (plan.Tool is "tenders" or "requests")
                        plan = new AiToolCall(AiToolCall.Classic, new Dictionary<string, string> { ["number"] = contextNumber });
                    else if (plan.Tool is "offers" or "counts" or "evaluation_progress" or "votes" && !plan.Args.ContainsKey("tender_number"))
                        plan.Args["tender_number"] = contextNumber;
                }

                if (plan != null && plan.Tool is not (AiToolCall.Classic or AiToolCall.None))
                    data = await _data.RunToolAsync(role, userId, plan, cancellationToken);

                if (data is null)
                {
                    if (plan?.Tool == AiToolCall.None)
                    {
                        data = await _data.OverviewAsync(role, userId, cancellationToken);
                    }
                    else
                    {
                        // "this tender" without a number: borrow the number of the record on the screen, for the lookup only
                        var forLookup = lookupQuestion;
                        if (AiDataService.RecordNumber(forLookup) == null)
                        {
                            var named = plan?.Arg("number");
                            if (!string.IsNullOrEmpty(named)) forLookup += " " + named;
                            else if (contextNumber != null && PointsAtThis(forLookup)) forLookup += " " + contextNumber;
                        }
                        data = await _data.LoadAsync(role, userId, forLookup, cancellationToken);
                    }
                }

                if (!data.IsSnapshot)
                    await _audit.LogAsync("AiDataAccess", "AiChat", data.Lookup, $"{data.Table?.Rows.Count ?? 0} row(s) shown to the user");   // never the question text
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "AI chat data lookup failed");
                data = null;
            }
        }

        // Passages of the company documents this role may read (AI-18). A failure here must not break the chat either.
        string? sources = null;
        if (sourcesFor != null && CompanyDataAllowed && role != "")
        {
            try { sources = await sourcesFor(message, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Company document search failed");
                sources = null;
            }
        }

        // Passages of the allowed websites, only when the user switched web mode on for this question (AI-19).
        string? web = null;
        if (webFor != null && role != "")
        {
            try { web = await webFor(message, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Web search failed");
                web = null;
            }
        }

        var messages = new List<AiChatMessage> { new("system", AiChatPrompts.BuildSystemPrompt(role, AiChatPrompts.CleanPage(page?.Split('?')[0]), data?.Facts,
            commandAvailable: _actions.Enabled && role == Roles.Admin, sources: sources, web: web)) };

        // The browser keeps the conversation, so treat it as untrusted: only user/assistant turns, limited in number and size.
        foreach (var turn in earlier)
            messages.Add(new AiChatMessage(turn.Role, turn.Text.Length > 4000 ? turn.Text[..4000] : turn.Text));
        messages.Add(new AiChatMessage("user", message));

        return new AiChatPrepared(null, messages, data?.Table, message, contextLabel);
    }

    // "this tender", "here", "it", هذه المناقصة ...: the user means the record on their screen
    private static readonly System.Text.RegularExpressions.Regex ThisPattern = new(@"\b(this|here|current|it|its)\b|هذا|هذه|الحالي", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static bool PointsAtThis(string question) => ThisPattern.IsMatch(question);

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
