using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Controllers;

// OWNER: Wadea (face enrollment). Face LOGIN lives in AccountController (FaceChallenge / FaceLogin).
//
// For Sara's onboarding page: the Face step only needs to show the widget
//     <partial name="_FaceCapture" model="@("enroll")" />
// and load  <script src="~/js/face.js"></script>  + <link rel="stylesheet" href="~/css/face.css" />.
// The widget talks to /Face/Begin and /Face/Enroll and raises a "face:enrolled" event on success.
[Authorize]
[AutoValidateAntiforgeryToken]
public class FaceController : Controller
{
    private readonly FaceService _face;
    private readonly PasskeyService _passkeys;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;

    public FaceController(FaceService face, PasskeyService passkeys, UserManager<ApplicationUser> users, AuditService audit)
    {
        _face = face;
        _passkeys = passkeys;
        _users = users;
        _audit = audit;
    }

    // GET /Face : a standalone page to register (or add) a face. Also useful from "Profile & security".
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Count"] = await _face.CountAsync(_users.GetUserId(User)!);
        return View();
    }

    // POST /Face/Begin : "do these moves" request for enrollment
    [HttpPost]
    public IActionResult Begin() => Json(_face.CreateChallenge("enroll", _users.GetUserId(User)!));

    // POST /Face/Enroll : the captured frames; saves an encrypted template when everything checks out
    [HttpPost]
    public async Task<IActionResult> Enroll([FromBody] FaceSubmitRequest request)
    {
        var userId = _users.GetUserId(User)!;
        var analysis = await _face.AnalyzeAsync(request.ChallengeId, "enroll", userId, request.Frames);
        if (!analysis.Ok) return Json(new { ok = false, message = analysis.Reason });

        var count = await _face.EnrollAsync(userId, analysis.Embedding!, analysis.Liveness);
        await _audit.LogAsync("FaceEnrolled", "ApplicationUser", userId, "Face saved for sign-in");
        return Json(new { ok = true, message = "Your face was saved. You can now sign in with it.", count });
    }

    // POST /Face/Remove : forget one of MY saved faces (the Profile & security page calls this)
    [HttpPost]
    public async Task<IActionResult> Remove([FromBody] FaceRemoveRequest request)
    {
        var userId = _users.GetUserId(User)!;

        // Same rule as for fingerprints: once onboarding is finished at least ONE sign-in method must remain.
        // The last face can only be removed when a fingerprint device is saved.
        var me = await _users.GetUserAsync(User);
        if (me is not null && me.OnboardingStatus == Statuses.OnboardingStatus.Completed
            && await _face.CountAsync(userId) <= 1 && await _passkeys.CountAsync(userId) == 0)
            return Json(new { ok = false, message = "You need at least one way to sign in. Add a fingerprint device or another face first, then remove this one." });

        var removed = await _face.RemoveAsync(userId, request.Id);
        if (removed) await _audit.LogAsync("FaceRemoved", "ApplicationUser", userId, "Face removed");
        return Json(new { ok = removed, message = removed ? "" : "This face profile could not be removed." });
    }
}
