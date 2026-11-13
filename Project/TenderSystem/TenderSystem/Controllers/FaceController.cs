using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
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
    private readonly UserManager<ApplicationUser> _users;

    public FaceController(FaceService face, UserManager<ApplicationUser> users)
    {
        _face = face;
        _users = users;
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
        return Json(new { ok = true, message = "Your face was saved. You can now sign in with it.", count });
    }
}
