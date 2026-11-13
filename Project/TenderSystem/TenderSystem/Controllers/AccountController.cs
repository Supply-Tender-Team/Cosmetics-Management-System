using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Biometrics;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Models;
using TenderSystem.Services.Audit;

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

    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, ApplicationDbContext db, AuditService audit)
    {
        _signIn = signIn;
        _users = users;
        _db = db;
        _audit = audit;
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

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
    }

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

    private async Task LogAttemptAsync(string? userId, bool success, string? reason)
    {
        _db.BiometricAuthLogs.Add(new BiometricAuthLog
        {
            UserId = userId,
            Method = "Password",
            Success = success,
            FailureReason = reason,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 300)] : null,
        });
        await _db.SaveChangesAsync();
        await _audit.LogForUserAsync(userId, success ? "LoginSucceeded" : "LoginFailed",
            "ApplicationUser", userId ?? "Unknown", reason);
    }

    // ───────────────────────── Logout ─────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _signIn.SignOutAsync();
        if (userId != null)
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
        if (!RegisterSupplierViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Please choose a category.");
        if (await _db.SupplierProfiles.AnyAsync(p => p.TaxNumber == model.TaxNumber))
            ModelState.AddModelError(nameof(model.TaxNumber), "A company with this tax number is already registered.");
        if (await _users.FindByEmailAsync(model.Email) != null)
            ModelState.AddModelError(nameof(model.Email), "This email is already registered.");
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

        await _users.AddToRoleAsync(user, Roles.Supplier);
        _db.SupplierProfiles.Add(new Data.Entities.Identity.SupplierProfile
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
        });
        await _db.SaveChangesAsync();

        await _audit.LogForUserAsync(null, "Registered", "ApplicationUser", user.Id,
            "Supplier account registered; approval pending.");

        // TODO (Abdallah/Sara): upload of legal documents (SupplierDocument) and notification to the Admin.
        TempData["Success"] = "Thank you! Your registration was received. An Admin will review your company, and you can sign in once it is approved.";
        return RedirectToAction(nameof(Login));
    }

    // ───────────────────────── Access denied ─────────────────────────

    public IActionResult AccessDenied() => View();

    // TODO (Wadea, later): Passkey/Options, Passkey/Verify and Face/Verify endpoints go here.
}
