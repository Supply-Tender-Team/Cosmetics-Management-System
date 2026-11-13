using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Controllers;

// OWNER: Wadea (fingerprint registration). Fingerprint LOGIN lives in AccountController (PasskeyOptions / PasskeyLogin).
//
// For Sara's onboarding page: the Fingerprint step only needs the widget
//     <partial name="_PasskeyRegister" />
// plus  <script src="~/js/passkey.js"></script>  and  <link rel="stylesheet" href="~/css/face.css" />.
// It talks to /Passkey/RegisterOptions and /Passkey/Register and raises a "passkey:registered" event on success.
[Authorize]
[AutoValidateAntiforgeryToken]
public class PasskeyController : Controller
{
    private readonly PasskeyService _passkeys;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;

    public PasskeyController(PasskeyService passkeys, UserManager<ApplicationUser> users, AuditService audit)
    {
        _passkeys = passkeys;
        _users = users;
        _audit = audit;
    }

    // GET /Passkey : register this device or your phone, and see / remove your devices
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Devices"] = await _passkeys.ListAsync(_users.GetUserId(User)!);
        ViewData["SuggestedName"] = GuessDeviceName(Request.Headers.UserAgent.ToString());
        return View();
    }

    // POST /Passkey/RegisterOptions : the device is asked to create a key pair
    [HttpPost]
    public async Task<IActionResult> RegisterOptions([FromBody] PasskeyOptionsRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var begin = await _passkeys.BeginRegistrationAsync(user, request);
        return Json(new { optionsId = begin.OptionsId, options = begin.Options });
    }

    // POST /Passkey/Register : the device answered; we verify and save the PUBLIC key
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] PasskeyRegisterRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var result = await _passkeys.CompleteRegistrationAsync(user, request, HttpContext.RequestAborted);
        if (result.Ok) await _audit.LogAsync("PasskeyAdded", "ApplicationUser", user.Id, "Fingerprint / device unlock added");
        return Json(new { ok = result.Ok, message = result.Message, count = result.Count });
    }

    // POST /Passkey/Remove : forget one of MY devices
    [HttpPost]
    public async Task<IActionResult> Remove([FromBody] PasskeyRemoveRequest request)
    {
        var userId = _users.GetUserId(User)!;
        var removed = await _passkeys.RemoveAsync(userId, request.Id);
        if (removed) await _audit.LogAsync("PasskeyRemoved", "ApplicationUser", userId, "Fingerprint / device unlock removed");
        return Json(new { ok = removed });
    }

    private static string GuessDeviceName(string userAgent)
    {
        var os = userAgent.Contains("iPhone") ? "iPhone"
            : userAgent.Contains("iPad") ? "iPad"
            : userAgent.Contains("Android") ? "Android phone"
            : userAgent.Contains("Windows") ? "Windows"
            : userAgent.Contains("Mac OS") ? "Mac"
            : userAgent.Contains("Linux") ? "Linux"
            : "Device";
        var browser = userAgent.Contains("Edg/") ? "Edge" : userAgent.Contains("Firefox") ? "Firefox"
            : userAgent.Contains("Chrome") ? "Chrome" : userAgent.Contains("Safari") ? "Safari" : "";
        return string.IsNullOrEmpty(browser) ? os : $"{os} ({browser})";
    }
}
