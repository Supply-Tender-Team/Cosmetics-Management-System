using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Controllers;

/// <summary>
/// AI-17: the full-page assistant, like ChatGPT. Every employee role can use it, a Supplier cannot (suppliers keep the small floating chat).
/// The chats are private: every action below works only on the signed-in user's own chats.
/// </summary>
[Authorize(Roles = Roles.Admin + "," + Roles.Requester + "," + Roles.SourcingOfficer + "," + Roles.EvaluationCommittee + "," + Roles.SourcingManager + "," + Roles.TopCommittee)]
public class AssistantController : Controller
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly AiChatPageService _pages;
    private readonly UserManager<ApplicationUser> _users;

    public AssistantController(AiChatPageService pages, UserManager<ApplicationUser> users)
    {
        _pages = pages;
        _users = users;
    }

    public class SendRequest
    {
        public int? SessionId { get; set; }
        public string? Message { get; set; }
        public string? Page { get; set; }
    }

    public class RenameRequest
    {
        public int Id { get; set; }
        public string? Title { get; set; }
    }

    public class DeleteRequest
    {
        public int Id { get; set; }
    }

    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>My chats, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Json(await _pages.ListAsync(UserId, ct));
    }

    /// <summary>One of my chats with all its messages.</summary>
    [HttpGet]
    public async Task<IActionResult> Chat(int id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var chat = await _pages.GetAsync(UserId, id, ct);
        return chat is null ? NotFound(new { message = "This chat was not found." }) : Json(chat);
    }

    /// <summary>
    /// Asks a question and streams the answer back as lines of JSON ("application/x-ndjson"): {"type":"delta","data":{"text":"..."}}.
    /// The browser stops the answer by closing the request (Stop button); what was written until then is saved.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000)]
    public async Task<IActionResult> Send([FromBody] SendRequest? request, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";            // proxies must not hold the pieces back

        async Task Emit(string type, object data)
        {
            var line = JsonSerializer.Serialize(new { type, data }, Web);
            await Response.WriteAsync(line + "\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        try
        {
            await _pages.SendAsync(User, UserId, request?.SessionId, request?.Message, request?.Page, Emit, ct);
        }
        catch (OperationCanceledException)
        {
            // the browser closed the request: nothing more to write
        }
        return new EmptyResult();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename([FromBody] RenameRequest? request, CancellationToken ct) =>
        request != null && await _pages.RenameAsync(UserId, request.Id, request.Title, ct)
            ? Json(new { ok = true })
            : NotFound(new { message = "This chat was not found." });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] DeleteRequest? request, CancellationToken ct) =>
        request != null && await _pages.DeleteAsync(UserId, request.Id, ct)
            ? Json(new { ok = true })
            : NotFound(new { message = "This chat was not found." });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll(CancellationToken ct) =>
        Json(new { ok = true, deleted = await _pages.DeleteAllAsync(UserId, ct) });

    private string UserId => _users.GetUserId(User) ?? "";
}
