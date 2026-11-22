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
/// because both call <see cref="AiChatService.PrepareAsync"/>.
/// </summary>
public sealed class AiChatPageService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex RecordNumber = new(@"^[A-Za-z]{1,6}(-[A-Za-z]{1,6})*-\d[A-Za-z0-9-]{0,30}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly AiChatService _chat;
    private readonly AiChatPageOptions _options;
    private readonly AuditService _audit;
    private readonly ILogger<AiChatPageService> _log;
    private readonly Infrastructure.Options.AiChatOptions _chatOptions;
    private readonly KnowledgeBaseService _knowledge;
    private readonly WebAnswerService _web;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Infrastructure.Options.OllamaOptions _ollama;

    public AiChatPageService(ApplicationDbContext db, AiChatService chat, IOptions<AiChatPageOptions> options, AuditService audit,
        ILogger<AiChatPageService> log, IOptions<Infrastructure.Options.AiChatOptions> chatOptions, KnowledgeBaseService knowledge, WebAnswerService web,
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

    /// <summary>
    /// Answers one question and sends the answer to <paramref name="emit"/> piece by piece. Events: "session" (id and title),
    /// "delta" (text), "table", "done", "error" (message, status). A question that is refused (too long, too fast...) creates nothing.
    /// When the caller goes away (Stop button, closed tab) the part written so far is saved and marked as stopped.
    /// </summary>
    public async Task SendAsync(ClaimsPrincipal user, string userId, int? sessionId, string? message, string? page, bool web,
        Func<string, object, Task> emit, CancellationToken ct)
    {
        ChatSession? session = null;
        if (sessionId != null)
        {
            session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session is null)
            {
                await emit("error", new { message = "This chat was not found. It may have been deleted.", status = 404 });
                return;
            }
            if (await _db.ChatMessages.CountAsync(m => m.SessionId == session.Id, ct) >= _options.MaxMessagesPerChat)
            {
                await emit("error", new { message = "This chat is full. Please start a new chat.", status = 400 });
                return;
            }
        }

        // What the assistant remembers comes from the database, never from the browser.
        var stored = session is null ? new List<ChatMessage>() : await _db.ChatMessages.AsNoTracking()
            .Where(m => m.SessionId == session.Id).OrderByDescending(m => m.Id).Take(_chatOptions.MaxHistoryMessages * 2).ToListAsync(ct);
        stored.Reverse();
        var history = stored.Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new AiChatTurn(m.Role, m.Content, string.IsNullOrEmpty(m.RecordNumbers) ? null : m.RecordNumbers.Split(',')))
            .ToList();

        // Passages of the company documents this user's ROLE may read, for the prompt; the list of titles is shown under the answer.
        var sources = new List<KnowledgeSource>();
        async Task<string?> FindPassages(string question, CancellationToken token)
        {
            var role = AiChatPrompts.RoleOf(user);
            if (!await _knowledge.HasDocumentsForAsync(role, token)) return null;      // no document for this role: the prompt says nothing about documents

            // a very short follow-up ("and the deadline?") is searched together with the question before it
            var previous = history.LastOrDefault(t => t.Role == "user")?.Text;
            var query = question.Length < 30 && !string.IsNullOrWhiteSpace(previous) ? previous + "\n" + question : question;
            var hits = await _knowledge.SearchAsync(role, query, token);

            var (text, titles) = KnowledgeBaseService.NumberPassages(hits);
            sources.AddRange(titles.Select((t, i) => new KnowledgeSource(i + 1, t)));
            if (hits.Count > 0) await _audit.LogAsync("AiKnowledgeAccess", "KnowledgeDocument", "search", $"{hits.Count} passage(s) of {titles.Count} document(s) given to the assistant");   // never the question
            return text;
        }

        // Web mode (AI-19): only when the user switched it on for THIS question. Its pages are shown as links under the answer, after the documents.
        string? webNotice = null;
        var webSources = new List<KnowledgeSource>();
        Func<string, CancellationToken, Task<string?>>? findOnWeb = null;
        if (web)
        {
            findOnWeb = async (question, token) =>
            {
                var context = await _web.GetContextAsync(userId, question, token);
                webNotice = context.Notice;
                webSources.AddRange(context.Sources.Select(s => new KnowledgeSource(s.N, s.Title, s.Url)));
                return context.Text;
            };
        }

        var prepared = await _chat.PrepareAsync(user, userId, message, history, page, ct, FindPassages, findOnWeb);
        sources.AddRange(webSources);
        if (prepared.Error != null)
        {
            await emit("error", new { message = prepared.Error.Text, status = prepared.Error.Status });
            return;
        }

        if (session is null)
        {
            session = new ChatSession { UserId = userId, Title = TitleFrom(prepared.Question) };
            _db.ChatSessions.Add(session);
        }
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatMessages.Add(new ChatMessage { Session = session, Role = "user", Content = prepared.Question });
        await _db.SaveChangesAsync(CancellationToken.None);
        await emit("session", new { id = session.Id, title = session.Title });
        if (webNotice != null) await emit("notice", new { message = webNotice });
        if (sources.Count > 0) await emit("sources", sources);

        var answer = new StringBuilder();
        var stopped = false;
        string? error = null;
        var errorStatus = 502;

        // Streams one model's answer into "answer". A failure sets error/errorStatus; the user pressing Stop (or leaving) sets stopped.
        async Task StreamFrom(IAiChatProvider provider)
        {
            error = null;
            errorStatus = 502;
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(TimeSpan.FromSeconds(_chat.TimeoutSeconds));
            var pieces = provider.StreamAsync(prepared.Messages, idle.Token).GetAsyncEnumerator(idle.Token);
            try
            {
                while (await pieces.MoveNextAsync())
                {
                    answer.Append(pieces.Current);
                    idle.CancelAfter(TimeSpan.FromSeconds(_chat.TimeoutSeconds));    // the limit counts the silence between pieces, not the whole answer
                    await emit("delta", new { text = pieces.Current });
                }
            }
            catch (AiUnavailableException ex)
            {
                _log.LogWarning(ex, "AI chat provider unavailable");
                error = ex.Message;
                errorStatus = 503;
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) stopped = true;
                else { error = "The assistant took too long to answer. Please try a shorter question or try again."; errorStatus = 504; }
            }
            catch (IOException)
            {
                stopped = true;                                                       // the browser went away while we were writing
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "AI chat failed");                                  // the question itself is never logged
                error = "Something went wrong with the assistant. Please try again.";
            }
            finally
            {
                await pieces.DisposeAsync();
            }
        }

        await StreamFrom(_chat.Provider);

        // Backup model (AI-20): the main model gave no answer at all (not running, not installed, too slow), so a LOCAL backup model tries once.
        var backup = BackupProvider();
        if (error != null && !stopped && answer.Length == 0 && backup != null)
        {
            await TryEmit(emit, "notice", new { message = "The main AI model did not answer, so the backup model is answering. The answer may be simpler." });
            await StreamFrom(backup);
        }

        var text = answer.ToString().Trim();
        if (text.Length == 0)
        {
            if (error is null && !stopped) { error = "The assistant did not give an answer. Please rephrase your question."; }
            if (error != null) await TryEmit(emit, "error", new { message = error, status = errorStatus });
            return;
        }

        var table = error is null && !stopped ? prepared.Table : null;
        var numbers = table is null ? null : string.Join(',', table.Rows.Select(r => r.Length > 0 ? r[0] : "")
            .Where(v => RecordNumber.IsMatch(v ?? "")).Take(25));
        var saved = new ChatMessage
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = text,
            TableJson = table is null ? null : JsonSerializer.Serialize(table, Json),
            SourcesJson = sources.Count == 0 ? null : JsonSerializer.Serialize(sources, Json),
            RecordNumbers = string.IsNullOrEmpty(numbers) ? null : numbers,
            Stopped = stopped || error != null,
        };
        _db.ChatMessages.Add(saved);
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);

        if (table != null) await TryEmit(emit, "table", table);
        if (error != null) await TryEmit(emit, "error", new { message = error, status = errorStatus });
        await TryEmit(emit, "done", new { id = saved.Id, stopped = saved.Stopped });
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
