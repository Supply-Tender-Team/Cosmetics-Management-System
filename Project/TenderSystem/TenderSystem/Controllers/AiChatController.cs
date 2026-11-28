using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Voice;
using System.Collections.Concurrent;

namespace TenderSystem.Controllers;

/// <summary>The floating assistant chat (partial Views/Shared/_AiChat.cshtml). Any signed-in user, answers depend on the role.</summary>
[Authorize]
public class AiChatController : Controller
{
    private readonly QuestionService _chat;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly AiNudgeService _nudges;
    private readonly VoiceTranscriptionService _voice;

    private const int MaxVoiceBytes = 976384;                     // 30 seconds of 16 kHz mono 16-bit audio, like the Admin voice search
    private static readonly RateLimiter VoiceLimit = new();

    public AiChatController(QuestionService chat, UserManager<ApplicationUser> users, AuditService audit, AiNudgeService nudges, VoiceTranscriptionService voice)
    {
        _chat = chat;
        _users = users;
        _audit = audit;
        _nudges = nudges;
        _voice = voice;
    }

    /// <summary>
    /// AI-12: voice typing with the LOCAL Whisper model (VOICE_MODEL_PATH). The browser sends up to 30 seconds of 16 kHz mono WAV;
    /// the text goes back into the chat box and nothing is stored. Any signed-in user, 6 recordings per minute each.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1_100_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_100_000)]
    public async Task<IActionResult> Transcribe(IFormFile? audio)
    {
        var userId = _users.GetUserId(User) ?? "";
        if (audio is null || audio.Length < 44 || audio.Length > MaxVoiceBytes)
            return BadRequest(new { error = "Record up to 30 seconds of audio and try again." });

        if (!VoiceLimit.TryTake(userId, 6, TimeSpan.FromMinutes(1)))
            return StatusCode(429, new { error = "You are recording too fast. Please wait a moment." });

        try
        {
            await using var stream = audio.OpenReadStream();
            var text = await _voice.TranscribeAsync(stream, HttpContext.RequestAborted);
            return Json(new { text });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Audio must be a valid 16 kHz mono PCM WAV recording of up to 30 seconds." });
        }
        catch (FileNotFoundException)
        {
            return StatusCode(503, new { error = "Voice typing is unavailable because its local model is missing." });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(503, new { error = "Voice typing with the local model is not configured." });
        }
    }

    /// <summary>
    /// AI-12: what the little cloud above the round button may say. Only counts of the user's OWN work (see AiNudgeService), no model call.
    /// An empty list means "nothing to say": the page then shows no cloud.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Nudges(CancellationToken cancellationToken)
    {
        var items = await _nudges.GetAsync(AiChatPrompts.RoleOf(User), _users.GetUserId(User) ?? "", cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Json(new { items = items.Select(i => new { text = i.Text, ask = i.Ask, link = i.Link }) });
    }

    public class AskRequest
    {
        public string? Message { get; set; }
        public string? Page { get; set; }
        public List<AiChatTurn>? History { get; set; }
    }

    public class ExportRequest
    {
        public string? Title { get; set; }
        public List<string>? Columns { get; set; }
        public List<List<string>>? Rows { get; set; }
    }

    /// <summary>
    /// AI-11: "Export to Excel" for a table shown in the chat. The browser posts the table it is showing and gets an .xlsx of exactly that
    /// (every cell as text, size caps in <see cref="AiTableExcelWriter"/>). Signed-in users only; nothing is stored.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> ExportTable([FromBody] ExportRequest? request)
    {
        var columns = request?.Columns;
        var rows = request?.Rows;
        if (columns is null || rows is null || columns.Count == 0 || columns.Count > AiTableExcelWriter.MaxColumns)
            return BadRequest(new { message = "There is no table to export." });
        if (rows.Count > AiTableExcelWriter.MaxRows)
            return BadRequest(new { message = $"The table has more than {AiTableExcelWriter.MaxRows} rows." });

        var bytes = AiTableExcelWriter.Write(request!.Title, columns, rows.Select(r => (IReadOnlyList<string>)(r ?? [])).ToList());
        await _audit.LogAsync("AiTableExported", "AiChat", "table", $"{rows.Count} row(s)");       // never the cells
        return File(bytes, AiTableExcelWriter.ContentType, $"assistant-table-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User) ?? "";
        var result = await _chat.AskAsync(User, userId, request?.Message, request?.History, request?.Page, cancellationToken);
        return StatusCode(result.Status, result.Ok ? new { ok = true, reply = result.Text, table = result.Table } : new { ok = false, message = result.Text });
    }
}
