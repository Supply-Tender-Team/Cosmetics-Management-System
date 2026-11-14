using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Services.Ai;

namespace TenderSystem.Controllers;

/// <summary>The floating assistant chat (partial Views/Shared/_AiChat.cshtml). Any signed-in user, answers depend on the role.</summary>
[Authorize]
public class AiChatController : Controller
{
    private readonly AiChatService _chat;
    private readonly UserManager<ApplicationUser> _users;

    public AiChatController(AiChatService chat, UserManager<ApplicationUser> users)
    {
        _chat = chat;
        _users = users;
    }

    public class AskRequest
    {
        public string? Message { get; set; }
        public string? Page { get; set; }
        public List<AiChatTurn>? History { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User) ?? "";
        var result = await _chat.AskAsync(User, userId, request?.Message, request?.History, request?.Page, cancellationToken);
        return StatusCode(result.Status, result.Ok ? new { ok = true, reply = result.Text } : new { ok = false, message = result.Text });
    }
}
