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

// OWNER: Wadea (login, later passkeys + face login, supplier registration)
// Login today: email + password. Fingerprint and face login are added later as extra actions here
// (they sign the user in with the same _signIn.SignInWithClaimsAsync call used below).
public class AccountController : Controller
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
            return Fail(model, "Too many failed attempts. Your account is locked for 15 minutes.");
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
        await _signIn.SignInWithClaimsAsync(user, model.RememberMe, [new Claim("FullName", user.FullName)]);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
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

    // ───────────────────────── Face login ─────────────────────────
    // The camera widget (wwwroot/js/face.js) calls these two endpoints. All checking happens here on the server.

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    // POST /Account/FaceChallenge : "do these moves" request. Same answer for any email (no account guessing).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult FaceChallenge([FromBody] FaceChallengeRequest request) =>
        Json(_face.CreateChallenge("login", NormalizeEmail(request.Email)));

    // POST /Account/FaceLogin : the captured frames. Signs the user in when the movement is live AND the face matches.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FaceLogin([FromBody] FaceSubmitRequest request)
    {
        const string notRecognised = "Face not recognised. Please try again, or sign in with your password.";

        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrEmpty(email)) return await FaceIdentifyLogin(request, notRecognised);   // "smart": no email typed

        var user = string.IsNullOrEmpty(email) ? null : await _users.FindByEmailAsync(email);

        // Always analyse the frames: an unknown email costs the same time and gives the same answers as a known one.
        var analysis = await _face.AnalyzeAsync(request.ChallengeId, "login", email, request.Frames);

        if (user == null)
        {
            await LogAttemptAsync(null, false, "Unknown email", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = analysis.Ok ? notRecognised : analysis.Reason });
        }

        if (await _users.IsLockedOutAsync(user))
        {
            await LogAttemptAsync(user.Id, false, "Locked out", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = "Too many failed attempts. Your account is locked for 15 minutes." });
        }

        if (!analysis.Ok)
        {
            // a bad movement is a user mistake, not an attack: it does not count toward the lockout
            await LogAttemptAsync(user.Id, false, "Liveness: " + analysis.Reason, Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = analysis.Reason });
        }

        var match = await _face.MatchAsync(user.Id, analysis.Embedding!);
        if (!match.Ok)
        {
            await _users.AccessFailedAsync(user);   // wrong faces count toward the same lockout as wrong passwords
            await LogAttemptAsync(user.Id, false, $"Face mismatch (similarity {match.Similarity:0.00})", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = notRecognised });
        }

        var blocked = await BlockedReasonAsync(user);
        if (blocked != null)
        {
            await LogAttemptAsync(user.Id, false, "Account not active", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = blocked });
        }

        await _users.ResetAccessFailedCountAsync(user);
        await _signIn.SignInWithClaimsAsync(user, false, [new Claim("FullName", user.FullName)]);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        await LogAttemptAsync(user.Id, true, null, Statuses.AuthMethod.Face);

        return Json(new { ok = true, redirectUrl = await LandingUrlAsync(user) });
    }

    /// <summary>
    /// Smart sign-in without typing the email: the face is searched among all enrolled people.
    /// Same liveness check as always; failures are throttled per client address because there is no account to lock.
    /// </summary>
    private async Task<IActionResult> FaceIdentifyLogin(FaceSubmitRequest request, string notRecognised)
    {
        if (!_face.AllowIdentify)
            return Json(new { ok = false, message = "Please type your email first, then press the face button." });

        var client = Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_face.IsThrottled(client))
            return Json(new { ok = false, message = "Too many attempts. Please wait a few minutes, or sign in with your email and password." });

        var analysis = await _face.AnalyzeAsync(request.ChallengeId, "login", string.Empty, request.Frames);
        if (!analysis.Ok)
        {
            _face.RegisterFailure(client);
            await LogAttemptAsync(null, false, "Face identify, liveness: " + analysis.Reason, Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = analysis.Reason });
        }

        var who = await _face.IdentifyAsync(analysis.Embedding!);
        var user = who.Ok ? await _users.FindByIdAsync(who.UserId!) : null;
        if (user == null)
        {
            _face.RegisterFailure(client);
            await LogAttemptAsync(null, false, $"Face identify: no confident match (best {who.Best:0.00}, second {who.Second:0.00})", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = notRecognised });
        }

        if (await _users.IsLockedOutAsync(user))
        {
            await LogAttemptAsync(user.Id, false, "Locked out", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = "Too many failed attempts. Your account is locked for 15 minutes." });
        }

        var blocked = await BlockedReasonAsync(user);
        if (blocked != null)
        {
            await LogAttemptAsync(user.Id, false, "Account not active", Statuses.AuthMethod.Face);
            return Json(new { ok = false, message = blocked });
        }

        _face.ClearFailures(client);
        await _users.ResetAccessFailedCountAsync(user);
        await _signIn.SignInWithClaimsAsync(user, false, [new Claim("FullName", user.FullName)]);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        await LogAttemptAsync(user.Id, true, $"identified (best {who.Best:0.00}, second {who.Second:0.00})", Statuses.AuthMethod.Face);

        return Json(new { ok = true, redirectUrl = await LandingUrlAsync(user) });
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
        var client = Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_passkeys.IsThrottled(client))
            return Json(new { ok = false, message = "Too many attempts. Please wait a few minutes, or sign in with your email and password." });

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

        if (await _users.IsLockedOutAsync(user))
        {
            await LogAttemptAsync(user.Id, false, "Locked out", Statuses.AuthMethod.Fingerprint);
            return Json(new { ok = false, message = "Too many failed attempts. Your account is locked for 15 minutes." });
        }

        var blocked = await BlockedReasonAsync(user);
        if (blocked != null)
        {
            await LogAttemptAsync(user.Id, false, "Account not active", Statuses.AuthMethod.Fingerprint);
            return Json(new { ok = false, message = blocked });
        }

        _passkeys.ClearFailures(client);
        await _users.ResetAccessFailedCountAsync(user);
        await _signIn.SignInWithClaimsAsync(user, false, [new Claim("FullName", user.FullName)]);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        await LogAttemptAsync(user.Id, true, null, Statuses.AuthMethod.Fingerprint);

        return Json(new { ok = true, redirectUrl = await LandingUrlAsync(user) });
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

    // ───────────────────────── Supplier registration ─────────────────────────

    [HttpGet]
    public IActionResult RegisterSupplier()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect(DashboardRouter.UrlFor(User));
        return View(new RegisterSupplierViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterSupplier(RegisterSupplierViewModel model)
    {
        if (!model.AcceptTerms)
            ModelState.AddModelError(nameof(model.AcceptTerms), "You must accept the terms to register.");

        // Website is optional. Accept "www.example.com" and add https:// ourselves.
        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            var site = model.Website.Trim();
            if (!site.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                site = "https://" + site;

            if (Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Host.Contains('.'))
                model.Website = uri.ToString().TrimEnd('/');
            else
                ModelState.AddModelError(nameof(model.Website), "Enter a valid website, for example www.example.com");
        }

        if (!RegisterSupplierViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Please choose a category.");
        if (await _db.SupplierProfiles.AnyAsync(p => p.TaxNumber == model.TaxNumber))
            ModelState.AddModelError(nameof(model.TaxNumber), "A company with this tax number is already registered.");
        if (await _users.FindByEmailAsync(model.Email) != null)
            ModelState.AddModelError(nameof(model.Email), "This email is already registered.");
        var uploads = new List<(string Type, IFormFile File, DateTime? Expiry)>();
        CheckDocument(model.CommercialRegistrationFile, nameof(model.CommercialRegistrationFile), "Commercial registration", true,
            model.CommercialRegistrationExpiry, nameof(model.CommercialRegistrationExpiry), uploads);
        CheckDocument(model.TaxCertificateFile, nameof(model.TaxCertificateFile), "Tax certificate", true,
            model.TaxCertificateExpiry, nameof(model.TaxCertificateExpiry), uploads);
        CheckDocument(model.OtherDocumentFile, nameof(model.OtherDocumentFile), "Other document", false, null, null, uploads);
        if (!ModelState.IsValid) return View(model);

        // The account stays inactive until the Admin approves the company.
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.ContactPerson,
            UserType = "Supplier",
            PhoneNumber = model.ContactPhone,
            IsActive = false,
        };
        var created = await _users.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        var stored = new List<(string Type, StoredFile File, DateTime? Expiry)>();
        try
        {
            foreach (var u in uploads)
            {
                await using var stream = u.File.OpenReadStream();
                stored.Add((u.Type, await _files.SaveAsync(stream, u.File.FileName, u.File.ContentType, HttpContext.RequestAborted), u.Expiry));
            }
        }
        catch (InvalidOperationException ex)
        {
            await _users.DeleteAsync(user);          // nobody is left with an account and no documents
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        await _users.AddToRoleAsync(user, Roles.Supplier);
        var profile = new Data.Entities.Identity.SupplierProfile
        {
            UserId = user.Id,
            CompanyName = model.CompanyName,
            TaxNumber = model.TaxNumber,
            CommercialRegNumber = model.CommercialRegNumber,
            Category = model.Category,
            Country = model.Country,
            City = model.City,
            Address = model.Address,
            Website = model.Website,
            ContactPerson = model.ContactPerson,
            ContactPhone = model.ContactPhone,
            Status = "Pending",
        };
        foreach (var d in stored)
            profile.SupplierDocuments.Add(new Data.Entities.Identity.SupplierDocument
            {
                DocType = d.Type,
                FilePath = d.File.RelativePath,
                ExpiryDate = d.Expiry,
                UploadedAt = DateTime.UtcNow,
            });
        _db.SupplierProfiles.Add(profile);
        await _db.SaveChangesAsync();

        await _audit.LogForUserAsync(null, "Registered", "ApplicationUser", user.Id,
            $"Supplier account registered; approval pending; {stored.Count} document(s).");

        await _notify.NotifyRoleAsync(Roles.Admin, "SupplierRegistered", "New supplier registration",
            $"{model.CompanyName} registered and is waiting for approval.", "/Admin/Suppliers");

        TempData["Success"] = "Thank you! Your registration was received. An Admin will review your company, and you can sign in once it is approved.";
        return RedirectToAction(nameof(Login));
    }

    // Checks one uploaded document (required or optional): type and size come from the UPLOAD_* settings in .env.
    private void CheckDocument(IFormFile? file, string field, string type, bool required, DateTime? expiry, string? expiryField,
        List<(string Type, IFormFile File, DateTime? Expiry)> uploads)
    {
        if (file is null || file.Length == 0)
        {
            if (required) ModelState.AddModelError(field, $"Please attach your {type.ToLowerInvariant()}.");
            return;
        }

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (string.IsNullOrWhiteSpace(extension) || !_upload.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(field, "This file type is not allowed. Allowed: " + string.Join(", ", _upload.AllowedExtensions) + ".");
            return;
        }
        if (file.Length > _upload.MaxFileSizeMb * 1024L * 1024L)
        {
            ModelState.AddModelError(field, $"The file is larger than {_upload.MaxFileSizeMb} MB.");
            return;
        }
        if (expiry is not null && expiry.Value.Date < DateTime.UtcNow.Date && expiryField is not null)
        {
            ModelState.AddModelError(expiryField, "This document has already expired.");
            return;
        }
        uploads.Add((type, file, expiry?.Date));
    }

    // ───────────────────────── Forgot / reset password ─────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // The answer is the same whether or not the address exists, so nobody can probe for accounts.
        await _passwords.SendResetLinkAsync(model.Email,
            (email, token) => Url.Action(nameof(ResetPassword), "Account", new { email, token })!);

        return View("ForgotPasswordSent");
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token) =>
        View(new ResetPasswordViewModel { Email = email ?? string.Empty, Token = token ?? string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _passwords.ResetPasswordAsync(model.Email, model.Token, model.Password);
        if (result.Succeeded)
        {
            TempData["Success"] = "Your password was changed. You can sign in now.";
            return RedirectToAction(nameof(Login));
        }

        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    // ───────────────────────── Access denied ─────────────────────────

    public IActionResult AccessDenied() => View();

}
