using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;

namespace TenderSystem.Controllers;

/// <summary>The floating assistant chat (partial Views/Shared/_AiChat.cshtml). Any signed-in user, answers depend on the role.</summary>
[Authorize]
public class AiChatController : Controller
{
    private readonly AiChatService _chat;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;

    public AiChatController(AiChatService chat, UserManager<ApplicationUser> users, AuditService audit)
    {
        _chat = chat;
        _users = users;
        _audit = audit;
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
