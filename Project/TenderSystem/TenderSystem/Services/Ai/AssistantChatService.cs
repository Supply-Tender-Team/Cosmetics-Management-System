using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Ai;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Knowledge;
using TenderSystem.Services.Web;

namespace TenderSystem.Services.Ai;

public sealed record ChatSessionItem(int Id, string Title, DateTime UpdatedAt);
/// <summary>
/// What an answer was based on, as shown under it. A company document: [N] Title (Url is null). A web page (web mode): [WN] Title as a link (Url is set).
/// </summary>
public sealed record KnowledgeSource(int N, string Title, string? Url = null);
public sealed record ChatMessageItem(int Id, string Role, string Content, AiTable? Table, bool Stopped, DateTime CreatedAt, List<KnowledgeSource>? Sources = null, int? Feedback = null);
/// <summary>Counts only. DownWithSources = thumbs down on answers that used company documents or web pages.</summary>
public sealed record AssistantFeedbackStats(int Answers, int Up, int Down, int DownWithSources, int UpLast30Days, int DownLast30Days);
public sealed record ChatSessionDetail(int Id, string Title, List<ChatMessageItem> Messages);

/// <summary>
/// The full-page assistant (AI-17): saved private conversations and streamed answers.
/// Every method takes the signed-in user's id and only ever touches rows of that user, so a chat can never be read by someone else
/// (an Admin included). The rules for a question (length, speed limit, role-limited data, prompt) are the ones of the floating chat
/// because both go through the same steps of <see cref="QuestionService"/> (CheckQuestion, StartAsync, BuildMessages).
/// </summary>
public sealed class AssistantChatService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex RecordNumber = new(@"^[A-Za-z]{1,6}(-[A-Za-z]{1,6})*-\d[A-Za-z0-9-]{0,30}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly QuestionService _chat;
    private readonly AiChatPageOptions _options;
    private readonly AuditService _audit;
    private readonly ILogger<AssistantChatService> _log;
    private readonly Infrastructure.Options.AiChatOptions _chatOptions;
    private readonly KnowledgeBaseService _knowledge;
    private readonly WebAnswerService _web;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Infrastructure.Options.OllamaOptions _ollama;

    public AssistantChatService(ApplicationDbContext db, QuestionService chat, IOptions<AiChatPageOptions> options, AuditService audit,
        ILogger<AssistantChatService> log, IOptions<Infrastructure.Options.AiChatOptions> chatOptions, KnowledgeBaseService knowledge, WebAnswerService web,
        IHttpClientFactory httpFactory, IOptions<Infrastructure.Options.OllamaOptions> ollama)
    {
        _knowledge = knowledge;
        _web = web;
        _httpFactory = httpFactory;
        _ollama = ollama.Value;
        _db = db;
        _chat = chat;
        _options = options.Value;
        _audit = audit;
        _log = log;
        _chatOptions = chatOptions.Value;
    }

    public bool Enabled => _chat.Enabled;

    /// <summary>The record the user's previous page was about (AI-21), as a short label, or null.</summary>
    public Task<string?> ContextAsync(ClaimsPrincipal user, string userId, string? page, CancellationToken ct) => _chat.ContextLabelAsync(user, userId, page, ct);
    public int HistoryDays => Math.Max(1, _options.HistoryDays);
    public int MaxMessageChars => _chatOptions.MaxMessageChars;

    /// <summary>True when the "Web" switch may be shown: web mode is on, has a search key and at least one allowed website.</summary>
    public Task<bool> WebAvailableAsync(CancellationToken ct) => _web.AvailableAsync(ct);

    public async Task<List<ChatSessionItem>> ListAsync(string userId, CancellationToken ct) =>
        await _db.ChatSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(100)
            .Select(s => new ChatSessionItem(s.Id, s.Title, s.UpdatedAt))
            .ToListAsync(ct);

    public async Task<ChatSessionDetail?> GetAsync(string userId, int id, CancellationToken ct)
    {
        var session = await _db.ChatSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null) return null;

        var rows = await _db.ChatMessages.AsNoTracking().Where(m => m.SessionId == id).OrderBy(m => m.Id).ToListAsync(ct);
        return new ChatSessionDetail(session.Id, session.Title, rows
            .Select(m => new ChatMessageItem(m.Id, m.Role, m.Content, ReadTable(m.TableJson), m.Stopped, m.CreatedAt, ReadSources(m.SourcesJson), m.Feedback))
            .ToList());
    }

    public async Task<bool> RenameAsync(string userId, int id, string? title, CancellationToken ct)
    {
        title = Clean(title, 120);
        if (title.Length == 0) return false;
        var session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null) return false;
        session.Title = title;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string userId, int id, CancellationToken ct)
    {
        var count = await DeleteWhereAsync(s => s.Id == id && s.UserId == userId, ct);
        if (count > 0) await _audit.LogAsync("AiChatDeleted", "ChatSession", id.ToString(), "1 chat deleted by its owner");   // never the content
        return count > 0;
    }

    public async Task<int> DeleteAllAsync(string userId, CancellationToken ct)
    {
        var count = await DeleteWhereAsync(s => s.UserId == userId, ct);
        if (count > 0) await _audit.LogAsync("AiChatDeleted", "ChatSession", "all", $"{count} chat(s) deleted by their owner");
        return count;
    }

    /// <summary>Called by the clean-up job: removes every chat that had no new message for <see cref="HistoryDays"/> days.</summary>
    public async Task<int> DeleteOldAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-HistoryDays);
        return await DeleteWhereAsync(s => s.UpdatedAt < cutoff, ct);
    }

    // The messages go first (every foreign key of our tables is "Restrict"), both in one transaction so a chat is never left half deleted.
    private async Task<int> DeleteWhereAsync(System.Linq.Expressions.Expression<Func<ChatSession, bool>> which, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.ChatMessages.Where(m => _db.ChatSessions.Where(which).Select(s => s.Id).Contains(m.SessionId)).ExecuteDeleteAsync(ct);
        var count = await _db.ChatSessions.Where(which).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return count;
    }

    /// <summary>What one <see cref="SendAsync"/> call keeps while it works, so the steps below do not need long parameter lists.</summary>
    private sealed class SendRun
    {
        public required Func<string, object, Task> Emit { get; init; }
        public ChatSession? Session { get; set; }
        public List<AiChatTurn> History { get; set; } = [];
        public ChatContext Chat { get; set; } = null!;
        /// <summary>The documents the answer used, then the web pages (shown as a list under the answer).</summary>
        public List<KnowledgeSource> Sources { get; } = [];
        public string? WebNotice { get; set; }

        public StringBuilder Answer { get; } = new();
        public bool Stopped { get; set; }
        public string? Error { get; set; }
        public int ErrorStatus { get; set; } = 502;
    }

    /// <summary>
    /// Answers one question and sends the answer to <paramref name="emit"/> piece by piece. Events: "session" (id and title),
    /// "delta" (text), "table", "done", "error" (message, status). A question that is refused (too long, too fast...) creates nothing.
    /// When the caller goes away (Stop button, closed tab) the part written so far is saved and marked as stopped.
    /// </summary>
    public async Task SendAsync(ClaimsPrincipal user, string userId, int? sessionId, string? message, string? page, bool web,
        Func<string, object, Task> emit, CancellationToken ct)
    {
        var run = new SendRun { Emit = emit };

        if (!await OpenChatAsync(run, userId, sessionId, ct)) return;
        await LoadHistoryAsync(run, ct);

        var (refused, question) = _chat.CheckQuestion(userId, message);
        if (refused != null)
        {
            await emit("error", new { message = refused.Text, status = refused.Status });
            return;
        }

        run.Chat = await _chat.StartAsync(user, userId, question, run.History, page, ct);
        await AddDocumentPassagesAsync(run, ct);
        if (web) await AddWebPassagesAsync(run, ct);
        _chat.BuildMessages(run.Chat);

        await SaveQuestionAsync(run, ct);
        await StreamAnswerAsync(run, ct);
        await SaveAnswerAsync(run);
    }

    // ------------------------------------------------------------------ the steps of SendAsync

    /// <summary>Finds the user's chat (a new chat is created later, when the first question is saved). False = an error was sent and the work stops.</summary>
    private async Task<bool> OpenChatAsync(SendRun run, string userId, int? sessionId, CancellationToken ct)
    {
        if (sessionId == null) return true;

        run.Session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (run.Session is null)
        {
            await run.Emit("error", new { message = "This chat was not found. It may have been deleted.", status = 404 });
            return false;
        }
        if (await _db.ChatMessages.CountAsync(m => m.SessionId == run.Session.Id, ct) >= _options.MaxMessagesPerChat)
        {
            await run.Emit("error", new { message = "This chat is full. Please start a new chat.", status = 400 });
            return false;
        }
        return true;
    }

    /// <summary>What the assistant remembers comes from the database, never from the browser.</summary>
    private async Task LoadHistoryAsync(SendRun run, CancellationToken ct)
    {
        if (run.Session is null) return;

        var stored = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.SessionId == run.Session.Id).OrderByDescending(m => m.Id).Take(_chatOptions.MaxHistoryMessages * 2).ToListAsync(ct);
        stored.Reverse();
        run.History = stored.Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new AiChatTurn(m.Role, m.Content, string.IsNullOrEmpty(m.RecordNumbers) ? null : m.RecordNumbers.Split(',')))
            .ToList();
    }

    /// <summary>Passages of the company documents this user's ROLE may read (AI-18). A failure here must not break the chat.</summary>
    private async Task AddDocumentPassagesAsync(SendRun run, CancellationToken ct)
    {
        var chat = run.Chat;
        if (!_chat.CompanyDataAllowed || chat.Role == "") return;

        try
        {
            if (!await _knowledge.HasDocumentsForAsync(chat.Role, ct)) return;      // no document for this role: the prompt says nothing about documents

            // a very short follow-up ("and the deadline?") is searched together with the question before it
            var previous = run.History.LastOrDefault(t => t.Role == "user")?.Text;
            var query = chat.Question.Length < 30 && !string.IsNullOrWhiteSpace(previous) ? previous + "\n" + chat.Question : chat.Question;
            var hits = await _knowledge.SearchAsync(chat.Role, query, ct);

            var (text, titles) = KnowledgeBaseService.NumberPassages(hits);
            run.Sources.AddRange(titles.Select((t, i) => new KnowledgeSource(i + 1, t)));
            chat.Sources = text;
            if (hits.Count > 0) await _audit.LogAsync("AiKnowledgeAccess", "KnowledgeDocument", "search", $"{hits.Count} passage(s) of {titles.Count} document(s) given to the assistant");   // never the question
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Company document search failed");
            chat.Sources = null;
        }
    }

    /// <summary>Web mode (AI-19): only when the user switched it on for THIS question. Its pages are shown as links under the answer, after the documents.</summary>
    private async Task AddWebPassagesAsync(SendRun run, CancellationToken ct)
    {
        var chat = run.Chat;
        if (chat.Role == "") return;

        try
        {
            var context = await _web.GetContextAsync(chat.UserId, chat.Question, ct);
            run.WebNotice = context.Notice;
            run.Sources.AddRange(context.Sources.Select(s => new KnowledgeSource(s.N, s.Title, s.Url)));
            chat.Web = context.Text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Web search failed");
            chat.Web = null;
        }
    }

    /// <summary>Saves the question (and a new chat when this is the first question), then tells the browser what the answer will be based on.</summary>
    private async Task SaveQuestionAsync(SendRun run, CancellationToken ct)
    {
        if (run.Session is null)
        {
            run.Session = new ChatSession { UserId = run.Chat.UserId, Title = TitleFrom(run.Chat.Question) };
            _db.ChatSessions.Add(run.Session);
        }
        run.Session.UpdatedAt = DateTime.UtcNow;
        _db.ChatMessages.Add(new ChatMessage { Session = run.Session, Role = "user", Content = run.Chat.Question });
        await _db.SaveChangesAsync(CancellationToken.None);

        await run.Emit("session", new { id = run.Session.Id, title = run.Session.Title });
        if (run.Chat.ContextLabel != null) await run.Emit("context", new { label = run.Chat.ContextLabel });
        if (run.WebNotice != null) await run.Emit("notice", new { message = run.WebNotice });
        if (run.Sources.Count > 0) await run.Emit("sources", run.Sources);
    }

    /// <summary>Streams the answer of the main model. When it gave nothing at all, a LOCAL backup model tries once (AI-20).</summary>
    private async Task StreamAnswerAsync(SendRun run, CancellationToken ct)
    {
        await StreamFromAsync(run, _chat.Provider, ct);

        var backup = BackupProvider();
        if (run.Error != null && !run.Stopped && run.Answer.Length == 0 && backup != null)
        {
            await TryEmit(run.Emit, "notice", new { message = "The main AI model did not answer, so the backup model is answering. The answer may be simpler." });
            await StreamFromAsync(run, backup, ct);
        }
    }

    /// <summary>Streams one model's answer into run.Answer. A failure sets Error and ErrorStatus; the user pressing Stop (or leaving) sets Stopped.</summary>
    private async Task StreamFromAsync(SendRun run, IAiChatProvider provider, CancellationToken ct)
    {
        run.Error = null;
        run.ErrorStatus = 502;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(TimeSpan.FromSeconds(_chat.TimeoutSeconds));
        var pieces = provider.StreamAsync(run.Chat.Messages, idle.Token).GetAsyncEnumerator(idle.Token);
        try
        {
            while (await pieces.MoveNextAsync())
            {
                run.Answer.Append(pieces.Current);
                idle.CancelAfter(TimeSpan.FromSeconds(_chat.TimeoutSeconds));    // the limit counts the silence between pieces, not the whole answer
                await run.Emit("delta", new { text = pieces.Current });
            }
        }
        catch (AiUnavailableException ex)
        {
            _log.LogWarning(ex, "AI chat provider unavailable");
            run.Error = ex.Message;
            run.ErrorStatus = 503;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) run.Stopped = true;
            else { run.Error = "The assistant took too long to answer. Please try a shorter question or try again."; run.ErrorStatus = 504; }
        }
        catch (IOException)
        {
            run.Stopped = true;                                                   // the browser went away while we were writing
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AI chat failed");                                  // the question itself is never logged
            run.Error = "Something went wrong with the assistant. Please try again.";
        }
        finally
        {
            await pieces.DisposeAsync();
        }
    }

    /// <summary>Saves what was written (also a stopped or half answer) and tells the browser it is over.</summary>
    private async Task SaveAnswerAsync(SendRun run)
    {
        var text = run.Answer.ToString().Trim();
        if (text.Length == 0)
        {
            if (run.Error is null && !run.Stopped) run.Error = "The assistant did not give an answer. Please rephrase your question.";
            if (run.Error != null) await TryEmit(run.Emit, "error", new { message = run.Error, status = run.ErrorStatus });
            return;
        }

        var table = run.Error is null && !run.Stopped ? run.Chat.Table : null;
        var numbers = table is null ? null : string.Join(',', table.Rows.Select(r => r.Length > 0 ? r[0] : "")
            .Where(v => RecordNumber.IsMatch(v ?? "")).Take(25));
        var saved = new ChatMessage
        {
            SessionId = run.Session!.Id,
            Role = "assistant",
            Content = text,
            TableJson = table is null ? null : JsonSerializer.Serialize(table, Json),
            SourcesJson = run.Sources.Count == 0 ? null : JsonSerializer.Serialize(run.Sources, Json),
            RecordNumbers = string.IsNullOrEmpty(numbers) ? null : numbers,
            Stopped = run.Stopped || run.Error != null,
        };
        _db.ChatMessages.Add(saved);
        run.Session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);

        if (table != null) await TryEmit(run.Emit, "table", table);
        if (run.Error != null) await TryEmit(run.Emit, "error", new { message = run.Error, status = run.ErrorStatus });
        await TryEmit(run.Emit, "done", new { id = saved.Id, stopped = saved.Stopped });
    }

    private static async Task TryEmit(Func<string, object, Task> emit, string type, object data)
    {
        try { await emit(type, data); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { /* the browser is gone; the answer is saved */ }
    }

    /// <summary>The local backup model (AI_CHAT_FALLBACK_OLLAMA_MODEL), or null when none is set or it is the same model as the main one.</summary>
    private IAiChatProvider? BackupProvider()
    {
        var model = _options.FallbackOllamaModel?.Trim();
        if (string.IsNullOrEmpty(model)) return null;
        if (_chat.Provider is OllamaChatProvider && string.Equals(model, _ollama.Model, StringComparison.OrdinalIgnoreCase)) return null;
        return new OllamaChatProvider(_httpFactory, Options.Create(new Infrastructure.Options.OllamaOptions { BaseUrl = _ollama.BaseUrl, Model = model }));
    }

    /// <summary>
    /// Thumbs up (1), thumbs down (-1) or no rating (0) on one answer of the user's OWN chats. Returns false when the answer is not theirs.
    /// </summary>
    public async Task<bool> RateAsync(string userId, int messageId, int value, CancellationToken ct)
    {
        if (value is not (-1 or 0 or 1)) return false;
        var message = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.Role == "assistant" && m.Session.UserId == userId, ct);
        if (message is null) return false;
        message.Feedback = value == 0 ? null : value;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>How the answers were rated, in numbers only. An Admin sees these counts, never the chats (they are private).</summary>
    public async Task<AssistantFeedbackStats> FeedbackStatsAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var rated = _db.ChatMessages.AsNoTracking().Where(m => m.Role == "assistant" && m.Feedback != null);
        return new AssistantFeedbackStats(
            await _db.ChatMessages.CountAsync(m => m.Role == "assistant", ct),
            await rated.CountAsync(m => m.Feedback == 1, ct),
            await rated.CountAsync(m => m.Feedback == -1, ct),
            await rated.CountAsync(m => m.Feedback == -1 && m.SourcesJson != null, ct),
            await rated.CountAsync(m => m.Feedback == 1 && m.CreatedAt >= since, ct),
            await rated.CountAsync(m => m.Feedback == -1 && m.CreatedAt >= since, ct));
    }

    private static List<KnowledgeSource>? ReadSources(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<List<KnowledgeSource>>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static AiTable? ReadTable(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<AiTable>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static string TitleFrom(string question)
    {
        var line = question.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? question;
        var title = Clean(line, 60);
        return title.Length == 0 ? "New chat" : title;
    }

    private static string Clean(string? text, int max)
    {
        var value = Regex.Replace(text ?? "", @"[\u0000-\u001F\u007F]+", " ").Trim();
        return value.Length > max ? value[..max].TrimEnd() + "…" : value;
    }
}
