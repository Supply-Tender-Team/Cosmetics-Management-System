using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Biometrics;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Models;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Auth;
using TenderSystem.Services.Biometrics;
using TenderSystem.Services.Files;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Controllers;

// OWNER: Wadea. This file: sign in (email + password, face, fingerprint) and sign out.
// Supplier registration is in AccountController.Registration.cs, forgot / reset password in AccountController.Password.cs.
// All three ways to sign in end the same way: StartSessionAsync (cookie + last login), then LogAttemptAsync (log + audit).
public partial class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly PasswordService _passwords;
    private readonly FaceService _face;
    private readonly PasskeyService _passkeys;
    private readonly NotificationService _notify;
    private readonly IFileStorageService _files;
    private readonly UploadOptions _upload;

    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, ApplicationDbContext db, AuditService audit, PasswordService passwords, FaceService face, PasskeyService passkeys, NotificationService notify, IFileStorageService files, IOptions<UploadOptions> upload)
    {
        _face = face;
        _passkeys = passkeys;
        _notify = notify;
        _files = files;
        _upload = upload.Value;
        _signIn = signIn;
        _users = users;
        _db = db;
        _audit = audit;
        _passwords = passwords;
    }

    // ───────────────────────── Login ─────────────────────────

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect(DashboardRouter.UrlFor(User));
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var user = await _users.FindByEmailAsync(model.Email);
        if (user == null)
        {
            await LogAttemptAsync(null, false, "Unknown email");
            return Fail(model, "Invalid email or password.");
        }

        // 1) check the password (counts failures toward lockout)
        var check = await _signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            await LogAttemptAsync(user.Id, false, "Locked out");
            return Fail(model, LockedOutMessage);
        }
        if (!check.Succeeded)
        {
            await LogAttemptAsync(user.Id, false, "Wrong password");
            return Fail(model, "Invalid email or password.");
        }

        // 2) the account must be allowed to sign in
        var blocked = await BlockedReasonAsync(user);
        if (blocked != null)
        {
            await LogAttemptAsync(user.Id, false, "Account not active");
            return Fail(model, blocked);
        }

        // 3) sign in (the FullName claim feeds the dashboard header)
        await StartSessionAsync(user, model.RememberMe);
        await LogAttemptAsync(user.Id, true, null);

        if (user.MustChangePassword) return Redirect(ChangePasswordUrl);
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return Redirect(await LandingUrlAsync(user));
    }

    // First sign-in with a temporary password (an employee created by the Admin): the change-password step comes first.
    private const string ChangePasswordUrl = "/Shared/Onboarding/ChangePassword";

    private async Task<string> LandingUrlAsync(ApplicationUser user) =>
        user.MustChangePassword ? ChangePasswordUrl : DashboardRouter.UrlFor(await _users.GetRolesAsync(user));

    private IActionResult Fail(LoginViewModel model, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        model.Password = string.Empty;
        return View(model);
    }

    /// <summary>Returns a message when this account may not sign in yet, otherwise null.</summary>
    private async Task<string?> BlockedReasonAsync(ApplicationUser user)
    {
        if (user.UserType == "Supplier")
        {
            var status = await _db.SupplierProfiles.Where(p => p.UserId == user.Id).Select(p => p.Status).FirstOrDefaultAsync();
            return status switch
            {
                "Approved" => null,
                "Rejected" => "Your supplier registration was rejected. Please contact the sourcing team.",
                "Suspended" => "Your supplier account is suspended. Please contact the sourcing team.",
                _ => "Your registration is still waiting for Admin approval. You will be able to sign in once it is approved.",
            };
        }
        return user.IsActive ? null : "Your account is disabled. Please contact the Admin.";
    }

    private async Task LogAttemptAsync(string? userId, bool success, string? reason, string method = Statuses.AuthMethod.Password)
    {
        _db.BiometricAuthLogs.Add(new BiometricAuthLog
        {
            UserId = userId,
            Method = method,
            Success = success,
            FailureReason = reason,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 300)] : null,
        });
        await _db.SaveChangesAsync();
        await _audit.LogForUserAsync(userId, success ? "LoginSucceeded" : "LoginFailed",
            "ApplicationUser", userId ?? "Unknown", reason);
    }

    private const string LockedOutMessage = "Too many failed attempts. Your account is locked for 15 minutes.";
    private const string TooManyAttemptsMessage = "Too many attempts. Please wait a few minutes, or sign in with your email and password.";

    /// <summary>The address of the caller (behind Cloudflare the real one is in CF-Connecting-IP). Used to throttle face and fingerprint failures.</summary>
    private string ClientAddress() =>
        Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>Every way to sign in ends here: the cookie (the FullName claim feeds the dashboard header) and the last login time.</summary>
    private async Task StartSessionAsync(ApplicationUser user, bool rememberMe)
    {
        await _signIn.SignInWithClaimsAsync(user, rememberMe, [new Claim("FullName", user.FullName)]);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
    }

    // The three steps every face or fingerprint sign-in ends with. A refusal is logged and returned as the JSON the browser shows; null = carry on.
    private async Task<IActionResult?> RefuseIfLockedOutAsync(ApplicationUser user, string method)
    {
        if (!await _users.IsLockedOutAsync(user)) return null;
        await LogAttemptAsync(user.Id, false, "Locked out", method);
        return Json(new { ok = false, message = LockedOutMessage });
    }

    private async Task<IActionResult?> RefuseIfBlockedAsync(ApplicationUser user, string method)
    {
        var blocked = await BlockedReasonAsync(user);
        if (blocked == null) return null;
        await LogAttemptAsync(user.Id, false, "Account not active", method);
        return Json(new { ok = false, message = blocked });
    }

    /// <summary>The success path of a face or fingerprint sign-in: a good sign-in clears the failed-attempt count, like a good password does.</summary>
    private async Task<IActionResult> FinishBiometricLoginAsync(ApplicationUser user, string method, string? details)
    {
        await _users.ResetAccessFailedCountAsync(user);
        await StartSessionAsync(user, rememberMe: false);
        await LogAttemptAsync(user.Id, true, details, method);
        return Json(new { ok = true, redirectUrl = await LandingUrlAsync(user) });
    }

    // ───────────────────────── Face login ─────────────────────────
    // The camera widget (wwwroot/js/face.js) calls these two endpoints. All checking happens here on the server.

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    // POST /Account/FaceChallenge : "do these moves" request. Same answer for any email (no account guessing).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult FaceChallenge([FromBody] FaceChallengeRequest request)
    {
        // Sign-in is one step (look straight) when the anti-photo check is available. The browser asks for the strict version (with a head turn)
        // right after "not sure", and an address with many recent failures never gets the shortcut.
        var client = ClientAddress();
        var strict = request.Strict || _face.IsThrottled(client);
        return Json(_face.CreateChallenge("login", NormalizeEmail(request.Email), strict));
    }

    // POST /Account/FaceLogin : the captured frames. Signs the user in when the movement is live AND the face matches.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FaceLogin([FromBody] FaceSubmitRequest request)
    {
        const string notRecognised = "Face not recognised. Please try again, or sign in with your password.";

        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrEmpty(email)) return await FaceIdentifyLogin(request, notRecognised);   // "smart": no email typed

        var user = await _users.FindByEmailAsync(email);

        // Always analyse the frames: an unknown email costs the same time and gives the same answers as a known one.
        var analysis = await _face.AnalyzeAsync(request.ChallengeId, "login", email, request.Frames);
        var client = ClientAddress();
        // "not sure it is a live face" is answered the same for every email, so nobody can learn which accounts exist
        if (analysis.NeedsTurn) _face.RegisterFailure(client);   // cheap one-step probing of the anti-photo check is throttled per address

        if (user == null)
        {
            await LogAttemptAsync(null, false, "Unknown email", Statuses.AuthMethod.Face);
            return Json(new { ok = false, needTurn = analysis.NeedsTurn, message = analysis.Ok ? notRecognised : analysis.Reason });
        }

        if (await RefuseIfLockedOutAsync(user, Statuses.AuthMethod.Face) is { } lockedOut) return lockedOut;

        if (!analysis.Ok)
        {
            // a bad movement is a user mistake, not an attack: it does not count toward the lockout
            await LogAttemptAsync(user.Id, false, analysis.NeedsTurn ? "One-step check not sure: asked for a head turn" : "Liveness: " + analysis.Reason, Statuses.AuthMethod.Face);
            return Json(new { ok = false, needTurn = analysis.NeedsTurn, message = analysis.Reason });
        }

        var match = await _face.MatchAsync(user.Id, analysis.Embedding!);
        if (!match.Ok)
        {
            await _users.AccessFailedAsync(user);   // wrong faces count toward the same lockout as wrong passwords
            await LogAttemptAsync(user.Id, false, $"Face mismatch (similarity {match.Similarity:0.00})", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = notRecognised });
        }

        if (await RefuseIfBlockedAsync(user, Statuses.AuthMethod.Face) is { } blocked) return blocked;

        _face.ClearFailures(client);
        return await FinishBiometricLoginAsync(user, Statuses.AuthMethod.Face, null);
    }

    /// <summary>
    /// Smart sign-in without typing the email: the face is searched among all enrolled people.
    /// Same liveness check as always; failures are throttled per client address because there is no account to lock.
    /// </summary>
    private async Task<IActionResult> FaceIdentifyLogin(FaceSubmitRequest request, string notRecognised)
    {
        if (!_face.AllowIdentify)
            return Json(new { ok = false, message = "Please type your email first, then press the face button." });

        var client = ClientAddress();
        if (_face.IsThrottled(client))
            return Json(new { ok = false, message = TooManyAttemptsMessage });

        var analysis = await _face.AnalyzeAsync(request.ChallengeId, "login", string.Empty, request.Frames);
        if (!analysis.Ok)
        {
            _face.RegisterFailure(client);
            await LogAttemptAsync(null, false, analysis.NeedsTurn ? "Face identify: one-step check not sure, asked for a head turn" : "Face identify, liveness: " + analysis.Reason, Statuses.AuthMethod.Face);
            return Json(new { ok = false, needTurn = analysis.NeedsTurn, message = analysis.Reason });
        }

        var who = await _face.IdentifyAsync(analysis.Embedding!);
        var user = who.Ok ? await _users.FindByIdAsync(who.UserId!) : null;
        if (user == null)
        {
            _face.RegisterFailure(client);
            await LogAttemptAsync(null, false, $"Face identify: no confident match (best {who.Best:0.00}, second {who.Second:0.00})", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = notRecognised });
        }

        if (await RefuseIfLockedOutAsync(user, Statuses.AuthMethod.Face) is { } lockedOut) return lockedOut;

        if (await RefuseIfBlockedAsync(user, Statuses.AuthMethod.Face) is { } blocked) return blocked;

        _face.ClearFailures(client);
        return await FinishBiometricLoginAsync(user, Statuses.AuthMethod.Face, $"identified (best {who.Best:0.00}, second {who.Second:0.00})");
    }

    // ───────────────────────── Fingerprint login (passkey) ─────────────────────────
    // The script wwwroot/js/passkey.js calls these two endpoints. No email is needed: the device offers its passkeys.

    // POST /Account/PasskeyOptions : a one-time challenge for the device to sign
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PasskeyOptions([FromBody] PasskeyOptionsRequest request)
    {
        var begin = _passkeys.BeginLogin(request);
        return Json(new { optionsId = begin.OptionsId, options = begin.Options });
    }

    // POST /Account/PasskeyLogin : the device's signed answer. Signs the user in when the signature is valid.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyLogin([FromBody] PasskeyLoginRequest request)
    {
        var client = ClientAddress();
        if (_passkeys.IsThrottled(client))
            return Json(new { ok = false, message = TooManyAttemptsMessage });

        var result = await _passkeys.CompleteLoginAsync(request, HttpContext.RequestAborted);
        if (!result.Ok)
        {
            _passkeys.RegisterFailure(client);
            await LogAttemptAsync(result.UserId, false, "Passkey: " + result.Message, Statuses.AuthMethod.Fingerprint);
            return Json(new { ok = false, message = result.Message });
        }

        var user = await _users.FindByIdAsync(result.UserId!);
        if (user == null)
            return Json(new { ok = false, message = "Fingerprint not recognised. Please try again, or sign in with your password." });

        if (await RefuseIfLockedOutAsync(user, Statuses.AuthMethod.Fingerprint) is { } lockedOut) return lockedOut;

        if (await RefuseIfBlockedAsync(user, Statuses.AuthMethod.Fingerprint) is { } blocked) return blocked;

        _passkeys.ClearFailures(client);
        return await FinishBiometricLoginAsync(user, Statuses.AuthMethod.Fingerprint, null);
    }

    // ───────────────────────── Logout ─────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _signIn.SignOutAsync();

        // The cookie may belong to a user that was deleted since (database reset, account removed). Logging out must always
        // work, and the audit row points at the user with a foreign key, so write it only when the user still exists.
        if (userId != null && await _users.FindByIdAsync(userId) != null)
            await _audit.LogForUserAsync(userId, "LoggedOut", "ApplicationUser", userId);
        return RedirectToAction("Index", "Home");
    }

    // ───────────────────────── Access denied ─────────────────────────

    public IActionResult AccessDenied() => View();

}
