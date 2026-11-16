using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Controllers;

/// <summary>
/// The assistant's command center (Services/Ai/AiActionService.cs). Admin only. The browser sends only an action id and the
/// fingerprint / face proof: the text and the recipients were fixed on the server when the action was proposed.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
public class AiActionController : Controller
{
    private readonly AiActionService _actions;
    private readonly PasskeyService _passkeys;
    private readonly UserManager<ApplicationUser> _users;

    public AiActionController(AiActionService actions, PasskeyService passkeys, UserManager<ApplicationUser> users)
    {
        _actions = actions;
        _passkeys = passkeys;
        _users = users;
    }

    public class ProposeRequest { public string? Request { get; set; } }
    public class ActionRequest { public string ActionId { get; set; } = ""; public string? Origin { get; set; } }
    public class PasskeyApproveRequest : PasskeyLoginRequest { public string ActionId { get; set; } = ""; }
    public class FaceApproveRequest : FaceSubmitRequest { public string ActionId { get; set; } = ""; }

    private IActionResult Off() => Json(new { ok = false, message = "The command center is turned off." });

    private static object Body(AiActionResult r) => new { ok = r.Ok, message = r.Message, card = r.Card, sent = r.Sent, failed = r.Failed, state = r.State, rows = r.ResultRows };

    [HttpPost]
    public async Task<IActionResult> Propose([FromBody] ProposeRequest request, CancellationToken ct)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();
        return Json(Body(await _actions.ProposeAsync(User, admin.Id, admin.FullName, request?.Request, ct)));
    }

    // ---- AI-09: Excel templates. The file is read and checked by the server's code, never by the model.

    [HttpGet]
    public async Task<IActionResult> Template(string? kind, CancellationToken ct)
    {
        if (!_actions.Enabled) return NotFound();
        var (file, error) = await _actions.CreateTemplateAsync(User, kind ?? "", ct);
        if (file is null) return BadRequest(error);
        return File(file, AiExcelTemplates.ContentType, $"template-{kind}.xlsx");
    }

    [HttpPost]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> ProposeFile([FromForm] string? kind, [FromForm] string? channel, [FromForm] string? request, IFormFile? file, CancellationToken ct)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();
        if (file is null || file.Length == 0) return Json(new { ok = false, message = "Please attach the Excel file made from the template." });
        if (file.Length > 11 * 1024 * 1024) return Json(new { ok = false, message = "The file is too big." });

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        return Json(Body(await _actions.ProposeFileAsync(User, admin.Id, admin.FullName, kind ?? "", channel == "notify", request, file.FileName, memory.ToArray(), ct)));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel([FromBody] ActionRequest request)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();
        return Json(Body(await _actions.CancelAsync(admin.Id, request?.ActionId ?? "")));
    }

    // ---- fingerprint

    [HttpPost]
    public async Task<IActionResult> PasskeyOptions([FromBody] ActionRequest request)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();

        var action = _actions.FindOwned(admin.Id, request?.ActionId ?? "");
        if (action is null) return Json(new { ok = false, message = "That action is no longer waiting. Please ask again." });

        var begin = await _passkeys.BeginApprovalAsync(admin, action.Id, new PasskeyOptionsRequest { Origin = request!.Origin });
        if (begin is null) return Json(new { ok = false, message = "You have no fingerprint saved. Approve with your face, or set up a fingerprint first." });
        return Json(new { ok = true, optionsId = begin.OptionsId, options = begin.Options });
    }

    [HttpPost]
    public async Task<IActionResult> PasskeyApprove([FromBody] PasskeyApproveRequest request, CancellationToken ct)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();
        return Json(Body(await _actions.ApproveWithPasskeyAsync(admin, request.ActionId, request, ct)));
    }

    // ---- face

    [HttpPost]
    public async Task<IActionResult> FaceChallenge([FromBody] ActionRequest request)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();

        var challenge = _actions.BeginFace(admin.Id, request?.ActionId ?? "");
        if (challenge is null) return Json(new { ok = false, message = "That action is no longer waiting. Please ask again." });
        return Json(challenge);
    }

    [HttpPost]
    public async Task<IActionResult> FaceApprove([FromBody] FaceApproveRequest request)
    {
        if (!_actions.Enabled) return Off();
        var admin = await _users.GetUserAsync(User);
        if (admin is null) return Challenge();
        return Json(Body(await _actions.ApproveWithFaceAsync(admin, request.ActionId, request)));
    }
}
