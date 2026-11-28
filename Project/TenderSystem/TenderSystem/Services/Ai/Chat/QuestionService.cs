using System.Security.Claims;
using System.Text.RegularExpressions;
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
///
/// A question goes through the same steps on the floating chat and on the full page (AssistantChatService):
///   1. <see cref="CheckQuestion"/>  is it allowed? (assistant on, not empty, not too long, not too fast)
///   2. <see cref="StartAsync"/>     open a <see cref="ChatContext"/> and load the data this role may see
///   3. (full page only)             add document passages and web passages to the context
///   4. <see cref="BuildMessages"/>  turn the context into the conversation for the model
/// </summary>
public sealed class QuestionService
{
    // the service lives one request, so the speed limiter is static
    private static readonly RateLimiter QuestionLimit = new();

    // "this tender", "here", "it", هذه المناقصة ...: the user means the record on their screen
    private static readonly Regex ThisPattern = new(@"\b(this|here|current|it|its)\b|هذا|هذه|الحالي", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AiChatOptions _options;
    private readonly IEnumerable<IAiChatProvider> _providers;
    private readonly ILogger<QuestionService> _log;
    private readonly RoleDataService _data;
    private readonly AuditService _audit;

    private readonly AiActionOptions _actions;
    private readonly AiLookupPlanner _planner;

    public QuestionService(IOptions<AiChatOptions> options, IEnumerable<IAiChatProvider> providers, ILogger<QuestionService> log,
        RoleDataService data, AuditService audit, IOptions<AiActionOptions> actions, AiLookupPlanner planner)
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

    /// <summary>The provider chosen in .env (AI_CHAT_PROVIDER).</summary>
    public IAiChatProvider Provider => _providers.First(p => (_options.Provider == "ollama") == (p is OllamaChatProvider));

    /// <summary>Seconds without a new piece of the answer after which a streamed answer is given up.</summary>
    public int TimeoutSeconds => Math.Max(5, _options.TimeoutSeconds);

    // ------------------------------------------------------------------ the floating chat

    public async Task<AiChatResult> AskAsync(ClaimsPrincipal user, string userId, string? message, IEnumerable<AiChatTurn>? history, string? page, CancellationToken cancellationToken)
    {
        var (error, question) = CheckQuestion(userId, message);
        if (error != null) return error;

        var chat = await StartAsync(user, userId, question, history, page, cancellationToken);
        BuildMessages(chat);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            var answer = await Provider.CompleteAsync(chat.Messages, timeout.Token);
            return string.IsNullOrWhiteSpace(answer)
                ? new AiChatResult(false, "The assistant did not give an answer. Please rephrase your question.", 502)
                : new AiChatResult(true, answer, 200, chat.Table);
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

    // ------------------------------------------------------------------ step 1: is the question allowed?

    /// <summary>
    /// Checks the question and keeps the user under the per-minute limit. Returns the error to show (null = fine) and the trimmed question.
    /// </summary>
    public (AiChatResult? Error, string Question) CheckQuestion(string userId, string? message)
    {
        if (!Enabled)
            return (Refused("The AI assistant is turned off.", 503), "");

        var question = message?.Trim() ?? "";
        if (question.Length == 0)
            return (Refused("Please type a question.", 400), "");
        if (question.Length > _options.MaxMessageChars)
            return (Refused($"Please keep your question under {_options.MaxMessageChars} characters.", 400), "");

        if (!QuestionLimit.TryTake(userId, _options.MaxQuestionsPerMinute, TimeSpan.FromMinutes(1)))
            return (Refused("You are asking too fast. Please wait a moment and try again.", 429), "");

        return (null, question);
    }

    private static AiChatResult Refused(string text, int status) => new(false, text, status);

    // ------------------------------------------------------------------ step 2: open the context and load the data

    /// <summary>Opens the context of one question and loads the role-limited data for it. Failing to load the data never breaks the chat.</summary>
    public async Task<ChatContext> StartAsync(ClaimsPrincipal user, string userId, string question, IEnumerable<AiChatTurn>? history, string? page, CancellationToken ct)
    {
        // The browser keeps the conversation, so treat it as untrusted: only user/assistant turns, limited in number.
        var earlier = (history ?? []).Where(t => t.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(t.Text)).TakeLast(_options.MaxHistoryMessages).ToList();
        var chat = new ChatContext { User = user, UserId = userId, Role = AiChatPrompts.RoleOf(user), Question = question, Page = page, Earlier = earlier };

        if (DataAllowed && chat.Role != "")
            await LoadDataAsync(chat, ct);
        return chat;
    }

    // Real data for this question, limited to this user's role INSIDE the queries (see RoleDataService). Failing to load it
    // must never break the chat: the assistant then simply answers without data.
    private async Task LoadDataAsync(ChatContext chat, CancellationToken ct)
    {
        try
        {
            var openRecord = await FindOpenRecordAsync(chat, ct);
            var plan = await ChooseLookupAsync(chat, openRecord, ct);
            chat.Data = await RunLookupAsync(chat, plan, openRecord, ct);

            if (!chat.Data.IsSnapshot)
                await _audit.LogAsync("AiDataAccess", "AiChat", chat.Data.Lookup, $"{chat.Data.Table?.Rows.Count ?? 0} row(s) shown to the user");   // never the question text
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "AI chat data lookup failed");
            chat.Data = null;
        }
    }

    /// <summary>AI-21: the number of the request or tender the user is looking at (the page they came from), only when they may see it. Sets the label.</summary>
    private async Task<string?> FindOpenRecordAsync(ChatContext chat, CancellationToken ct)
    {
        var number = await _data.RecordNumberOfPageAsync(chat.Page, ct);
        if (number == null) return null;

        chat.ContextLabel = await _data.VisibleRecordLabelAsync(chat.Role, chat.UserId, number, ct);
        return chat.ContextLabel != null ? number : null;
    }

    /// <summary>AI-21: the model chooses which lookup fits (with the filters). Null when it cannot.</summary>
    private async Task<AiToolCall?> ChooseLookupAsync(ChatContext chat, string? openRecord, CancellationToken ct)
    {
        var seat = await _data.HasSeatAsync(chat.Role, chat.UserId, ct);
        var plan = await _planner.PlanAsync(chat.Role, seat, chat.Question, chat.Earlier, chat.ContextLabel, openRecord, ct);

        // "this tender" / "this request": whatever list the model picked, the question is about the record on the screen
        if (plan != null && openRecord != null && PointsAtThis(chat.Question))
            return PointAtOpenRecord(plan, openRecord);
        return plan;
    }

    private static AiToolCall PointAtOpenRecord(AiToolCall plan, string openRecord)
    {
        if (plan.Tool is "tenders" or "requests")
            return new AiToolCall(AiToolCall.Record, new Dictionary<string, string> { ["number"] = openRecord });

        if (plan.Tool is "offers" or "counts" or "evaluation_progress" or "votes" && !plan.Args.ContainsKey("tender_number"))
            plan.Args["tender_number"] = openRecord;
        if (plan.Tool == "help" && !plan.Args.ContainsKey("number"))
            plan.Args["number"] = openRecord;
        return plan;
    }

    /// <summary>Runs the chosen lookup. When there is none (or it found nothing) the overview follows, or the record card when the question has a number.</summary>
    private async Task<AiDataResult> RunLookupAsync(ChatContext chat, AiToolCall? plan, string? openRecord, CancellationToken ct)
    {
        if (plan != null && plan.Tool is not (AiToolCall.Record or AiToolCall.None))
        {
            var found = await _data.RunToolAsync(chat.Role, chat.UserId, plan, chat.Question, ct);
            if (found != null) return found;
        }

        if (plan?.Tool == AiToolCall.None)
            return await _data.OverviewAsync(chat.Role, chat.UserId, ct);

        return await _data.LoadAsync(chat.Role, chat.UserId, QuestionForLookup(chat, plan, openRecord), ct);
    }

    /// <summary>The question the keyword lookup reads: a follow-up borrows the number of an earlier turn, "this tender" the number on the screen. The model still gets the original words.</summary>
    private static string QuestionForLookup(ChatContext chat, AiToolCall? plan, string? openRecord)
    {
        var question = AiFollowUp.Resolve(chat.Question, chat.Earlier);
        if (RoleDataService.RecordNumber(question) != null) return question;

        var named = plan?.Arg("number");
        if (!string.IsNullOrEmpty(named)) return question + " " + named;
        if (openRecord != null && PointsAtThis(question)) return question + " " + openRecord;
        return question;
    }

    private static bool PointsAtThis(string question) => ThisPattern.IsMatch(question);

    // ------------------------------------------------------------------ step 4: the conversation for the model

    /// <summary>Fills <see cref="ChatContext.Messages"/>: the system prompt (role, data, documents, web), the earlier turns and the question.</summary>
    public void BuildMessages(ChatContext chat)
    {
        var page = AiChatPrompts.CleanPage(chat.Page?.Split('?')[0]);
        var commandAvailable = _actions.Enabled && chat.Role == Roles.Admin;
        chat.Messages.Add(new AiChatMessage("system", AiChatPrompts.BuildSystemPrompt(chat.Role, page, chat.Data?.Facts,
            commandAvailable: commandAvailable, sources: chat.Sources, web: chat.Web)));

        foreach (var turn in chat.Earlier)
            chat.Messages.Add(new AiChatMessage(turn.Role, turn.Text.Length > 4000 ? turn.Text[..4000] : turn.Text));
        chat.Messages.Add(new AiChatMessage("user", chat.Question));
    }
}
