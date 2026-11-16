using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Shared.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Models;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Auth;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class OnboardingController : Controller
{
    private const string InitialPasswordChangedClaim = "onboarding:initial-password-changed";
    private const string OnboardingStepClaim = "onboarding:step";
    private const string SecurityStep = "Security";       // the page where the user chooses fingerprint and/or face
    private const string FingerprintStep = "Fingerprint"; // old step name, kept so a user who was half way before this change is not lost
    private const string FaceStep = "Face";
    private const string FinishStep = "Finish";
    private static readonly HashSet<string> DemoEmployeeEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin@lumiere.local", "requester@lumiere.local", "officer@lumiere.local",
        "evaluator@lumiere.local", "manager@lumiere.local", "top@lumiere.local"
    };

    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly PasswordService _passwords;
    private readonly PasskeyService _passkeys;
    private readonly FaceService _face;
    private readonly AuditService _audit;

    public OnboardingController(UserManager<ApplicationUser> users, ApplicationDbContext db, IWebHostEnvironment environment,
        PasswordService passwords, PasskeyService passkeys, FaceService face, AuditService audit)
    {
        _users = users;
        _db = db;
        _environment = environment;
        _passwords = passwords;
        _passkeys = passkeys;
        _face = face;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.IsActive) return Forbid();
        if (user.MustChangePassword || await RequiresInitialPasswordChangeAsync(user))
            return RedirectToAction(nameof(ChangePassword));
        if (user.OnboardingStatus == "Completed")
            return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));

        if (await IsSupplierAsync(user))
        {
            var supplier = await _db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            if (GetMissingSupplierFields(user, supplier).Count > 0)
                return RedirectToAction(nameof(Supplier));
        }
        else
        {
            var employee = await _db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            if (GetMissingEmployeeFields(user, employee).Count > 0)
                return RedirectToAction(nameof(Profile));
        }

        return await GetOnboardingStepAsync(user) switch
        {
            FinishStep => RedirectToAction(nameof(Complete)),
            _ => RedirectToAction(nameof(Security)),      // Security, or an old Fingerprint / Face step
        };
    }

    [HttpGet]
    public async Task<IActionResult> ChangePassword()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.MustChangePassword && !await RequiresInitialPasswordChangeAsync(user)) return RedirectToAction(nameof(Index));
        return View(new ChangePasswordViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.MustChangePassword && !await RequiresInitialPasswordChangeAsync(user)) return RedirectToAction(nameof(Index));
        if (!ModelState.IsValid) return View(model);

        var result = await _passwords.ChangePasswordAsync(user.Id, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var claims = await _users.GetClaimsAsync(user);
        if (!claims.Any(claim => claim.Type == InitialPasswordChangedClaim))
        {
            var claimResult = await _users.AddClaimAsync(user, new Claim(InitialPasswordChangedClaim, "true"));
            if (!claimResult.Succeeded)
            {
                foreach (var error in claimResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return View(model);
            }
        }

        user.MustChangePassword = false;
        user.OnboardingStatus = "InProgress";
        var userUpdate = await _users.UpdateAsync(user);
        if (!userUpdate.Succeeded)
        {
            foreach (var error in userUpdate.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        var stepUpdate = await SetOnboardingStepAsync(user, SecurityStep);
        if (!stepUpdate.Succeeded)
        {
            foreach (var error in stepUpdate.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await IsSupplierAsync(user)) return Forbid();
        ViewBag.RoleLabel = Roles.LabelFor(User);
        await EmployeeNumbers.EnsureAsync(_db, user.Id, cancellationToken);   // the employee number is made by the system, never typed
        var profile = await _db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        var model = profile is null ? new EmployeeOnboardingViewModel { FullName = user.FullName } : new EmployeeOnboardingViewModel
        {
            FullName = user.FullName,
            EmployeeNumber = profile.EmployeeNumber,
            HireDate = profile.HireDate == default ? DateTime.Today : profile.HireDate,
            WorkPhone = profile.WorkPhone,
            PreferredLanguage = profile.PreferredLanguage
        };
        model.MissingFields = GetMissingEmployeeFields(user, profile);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(EmployeeOnboardingViewModel model, CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await IsSupplierAsync(user)) return Forbid();
        ViewBag.RoleLabel = Roles.LabelFor(User);
        await EmployeeNumbers.EnsureAsync(_db, user.Id, cancellationToken);   // the employee number is made by the system, never typed
        var profile = await _db.EmployeeProfiles.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        var missingFields = GetMissingEmployeeFields(user, profile);
        model.MissingFields = missingFields;
        if (missingFields.Count == 0) return RedirectToAction(nameof(Index));
        if (!ModelState.IsValid) return View(model);
        if (missingFields.Contains(nameof(model.EmployeeNumber))
            && await _db.EmployeeProfiles.AnyAsync(x => x.EmployeeNumber == model.EmployeeNumber && x.UserId != user.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.EmployeeNumber), "This employee number is already used.");
            return View(model);
        }

        if (missingFields.Contains(nameof(model.FullName))) user.FullName = model.FullName.Trim();
        user.OnboardingStatus = "InProgress";
        var userResult = await _users.UpdateAsync(user);
        if (!userResult.Succeeded)
        {
            foreach (var error in userResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        if (profile is null)
        {
            profile = new EmployeeProfile { UserId = user.Id };
            _db.EmployeeProfiles.Add(profile);
        }
        if (missingFields.Contains(nameof(model.EmployeeNumber))) profile.EmployeeNumber = model.EmployeeNumber.Trim();
        if (missingFields.Contains(nameof(model.HireDate))) profile.HireDate = model.HireDate;
        if (missingFields.Contains(nameof(model.WorkPhone))) profile.WorkPhone = model.WorkPhone.Trim();
        if (missingFields.Contains(nameof(model.PreferredLanguage))) profile.PreferredLanguage = model.PreferredLanguage;
        await _db.SaveChangesAsync(cancellationToken);
        var stepUpdate = await SetOnboardingStepAsync(user, SecurityStep);
        if (!stepUpdate.Succeeded)
        {
            foreach (var error in stepUpdate.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Supplier(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await IsSupplierAsync(user)) return Forbid();
        ViewBag.Categories = RegisterSupplierViewModel.Categories;
        var profile = await _db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        var model = profile is null ? new SupplierOnboardingViewModel { FullName = user.FullName, ContactPerson = user.FullName } : new SupplierOnboardingViewModel
        {
            FullName = user.FullName,
            CompanyName = profile.CompanyName,
            TaxNumber = profile.TaxNumber,
            CommercialRegNumber = profile.CommercialRegNumber,
            Country = profile.Country,
            City = profile.City,
            Address = profile.Address,
            ContactPerson = profile.ContactPerson,
            ContactPhone = profile.ContactPhone,
            Website = profile.Website,
            Category = profile.Category
        };
        model.MissingFields = GetMissingSupplierFields(user, profile);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Supplier(SupplierOnboardingViewModel model, CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await IsSupplierAsync(user)) return Forbid();
        ViewBag.Categories = RegisterSupplierViewModel.Categories;
        var profile = await _db.SupplierProfiles.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        var missingFields = GetMissingSupplierFields(user, profile);
        model.MissingFields = missingFields;
        if (missingFields.Count == 0) return RedirectToAction(nameof(Index));
        if (!ModelState.IsValid) return View(model);
        if (missingFields.Contains(nameof(model.TaxNumber))
            && await _db.SupplierProfiles.AnyAsync(x => x.TaxNumber == model.TaxNumber && x.UserId != user.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.TaxNumber), "This tax number is already registered.");
            return View(model);
        }

        if (missingFields.Contains(nameof(model.FullName))) user.FullName = model.FullName.Trim();
        user.OnboardingStatus = "InProgress";
        var userResult = await _users.UpdateAsync(user);
        if (!userResult.Succeeded)
        {
            foreach (var error in userResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        if (profile is null)
        {
            profile = new SupplierProfile { UserId = user.Id, Status = "Pending" };
            _db.SupplierProfiles.Add(profile);
        }
        if (missingFields.Contains(nameof(model.CompanyName))) profile.CompanyName = model.CompanyName.Trim();
        if (missingFields.Contains(nameof(model.TaxNumber))) profile.TaxNumber = model.TaxNumber.Trim();
        if (missingFields.Contains(nameof(model.CommercialRegNumber))) profile.CommercialRegNumber = model.CommercialRegNumber.Trim();
        if (missingFields.Contains(nameof(model.Country))) profile.Country = model.Country.Trim();
        if (missingFields.Contains(nameof(model.City))) profile.City = model.City.Trim();
        if (missingFields.Contains(nameof(model.Address))) profile.Address = model.Address.Trim();
        if (missingFields.Contains(nameof(model.ContactPerson))) profile.ContactPerson = model.ContactPerson.Trim();
        if (missingFields.Contains(nameof(model.ContactPhone))) profile.ContactPhone = model.ContactPhone.Trim();
        if (missingFields.Contains(nameof(model.Category))) profile.Category = model.Category.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        var stepUpdate = await SetOnboardingStepAsync(user, SecurityStep);
        if (!stepUpdate.Succeeded)
        {
            foreach (var error in stepUpdate.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        return RedirectToAction(nameof(Index));
    }

    // The user needs AT LEAST ONE way to sign in: fingerprint (or device unlock) or face. Both are allowed.
    // Security = the menu with the two options. Fingerprint and Face = the setup page of one option.
    private static readonly string[] SecurityPageSteps = [SecurityStep, FingerprintStep, FaceStep, FinishStep];

    // Null when the user may be on the security pages; otherwise where they must go instead.
    private async Task<IActionResult?> SecurityPageGuardAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (user.MustChangePassword || await RequiresInitialPasswordChangeAsync(user)
            || !await IsAtStepAsync(user, SecurityPageSteps) || await HasMissingProfileFieldsAsync(user, cancellationToken))
            return RedirectToAction(nameof(Index));
        return null;
    }

    private async Task LoadSecurityCountsAsync(ApplicationUser user)
    {
        ViewData["Fingerprints"] = await _passkeys.CountAsync(user.Id);
        ViewData["Faces"] = await _face.CountAsync(user.Id);
    }

    [HttpGet]
    public async Task<IActionResult> Security(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await SecurityPageGuardAsync(user, cancellationToken) is { } redirect) return redirect;
        await SetOnboardingStepAsync(user, SecurityStep);     // back on the menu (also when coming back from the final page)
        await LoadSecurityCountsAsync(user);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Security(bool continueStep, CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await SecurityPageGuardAsync(user, cancellationToken) is { } redirect) return redirect;
        await LoadSecurityCountsAsync(user);
        if ((int)ViewData["Fingerprints"]! + (int)ViewData["Faces"]! == 0)
        {
            ModelState.AddModelError(string.Empty, "Please set up at least one way to sign in: fingerprint or face. You can set up both.");
            return View();
        }
        var result = await SetOnboardingStepAsync(user, FinishStep);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }
        return RedirectToAction(nameof(Complete));
    }

    [HttpGet]
    public async Task<IActionResult> Fingerprint(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await SecurityPageGuardAsync(user, cancellationToken) is { } redirect) return redirect;
        ViewData["Count"] = await _passkeys.CountAsync(user.Id);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Face(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await SecurityPageGuardAsync(user, cancellationToken) is { } redirect) return redirect;
        ViewData["Count"] = await _face.CountAsync(user.Id);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Complete()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (user.MustChangePassword || !await IsAtStepAsync(user, FinishStep) || await HasMissingProfileFieldsAsync(user, HttpContext.RequestAborted))
            return RedirectToAction(nameof(Index));
        if (await SendBackToMissingBiometricAsync(user) is { } back) return back;
        await LoadSecurityCountsAsync(user);
        return View(new OnboardingConsentViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(OnboardingConsentViewModel model)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.MustChangePassword || !await IsAtStepAsync(user, FinishStep)
            || await HasMissingProfileFieldsAsync(user, HttpContext.RequestAborted))
            return RedirectToAction(nameof(Index));
        if (await SendBackToMissingBiometricAsync(user) is { } back) return back;
        if (!model.Accepted)
        {
            await LoadSecurityCountsAsync(user);
            ModelState.AddModelError(nameof(model.Accepted), "Please confirm to continue.");
            return View(model);
        }
        user.BiometricConsentAt = DateTime.UtcNow;
        user.OnboardingStatus = "Completed";
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            await LoadSecurityCountsAsync(user);
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        await ClearOnboardingStepAsync(user);
        var hasFingerprint = await _passkeys.CountAsync(user.Id) > 0;
        var hasFace = await _face.CountAsync(user.Id) > 0;
        await _audit.LogAsync("OnboardingCompleted", "ApplicationUser", user.Id,
            "Password and profile done. Sign-in methods: " + (hasFingerprint && hasFace ? "fingerprint and face" : hasFingerprint ? "fingerprint" : "face"));
        return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
    }

    // At least ONE of fingerprint or face is required (both are allowed). With none saved, the user goes back to the menu.
    private async Task<IActionResult?> SendBackToMissingBiometricAsync(ApplicationUser user)
    {
        if (await _passkeys.CountAsync(user.Id) + await _face.CountAsync(user.Id) == 0)
        {
            await SetOnboardingStepAsync(user, SecurityStep);
            return RedirectToAction(nameof(Security));
        }
        return null;
    }

    private async Task<bool> IsSupplierAsync(ApplicationUser user) =>
        user.UserType.Equals("Supplier", StringComparison.OrdinalIgnoreCase)
        || await _users.IsInRoleAsync(user, Roles.Supplier);

    private async Task<bool> HasMissingProfileFieldsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (await IsSupplierAsync(user))
        {
            var supplier = await _db.SupplierProfiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            return GetMissingSupplierFields(user, supplier).Count > 0;
        }

        var employee = await _db.EmployeeProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        return GetMissingEmployeeFields(user, employee).Count > 0;
    }

    private async Task<string> GetOnboardingStepAsync(ApplicationUser user)
    {
        var claim = (await _users.GetClaimsAsync(user))
            .FirstOrDefault(x => x.Type == OnboardingStepClaim);
        return claim?.Value ?? SecurityStep;
    }

    private async Task<bool> IsAtStepAsync(ApplicationUser user, params string[] steps)
    {
        var currentStep = await GetOnboardingStepAsync(user);
        return Array.IndexOf(steps, currentStep) >= 0;
    }

    private async Task<IdentityResult> SetOnboardingStepAsync(ApplicationUser user, string step)
    {
        var existingClaims = (await _users.GetClaimsAsync(user))
            .Where(x => x.Type == OnboardingStepClaim).ToList();
        if (existingClaims.Count == 1 && existingClaims[0].Value == step) return IdentityResult.Success;
        if (existingClaims.Count > 0)
        {
            var removeResult = await _users.RemoveClaimsAsync(user, existingClaims);
            if (!removeResult.Succeeded) return removeResult;
        }
        return await _users.AddClaimAsync(user, new Claim(OnboardingStepClaim, step));
    }

    private async Task ClearOnboardingStepAsync(ApplicationUser user)
    {
        var claims = (await _users.GetClaimsAsync(user))
            .Where(x => x.Type == OnboardingStepClaim).ToList();
        if (claims.Count > 0) await _users.RemoveClaimsAsync(user, claims);
    }

    private static HashSet<string> GetMissingEmployeeFields(ApplicationUser user, EmployeeProfile? profile)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(user.FullName)) missing.Add(nameof(EmployeeOnboardingViewModel.FullName));
        if (profile is null || string.IsNullOrWhiteSpace(profile.EmployeeNumber)) missing.Add(nameof(EmployeeOnboardingViewModel.EmployeeNumber));
        if (profile is null || profile.HireDate == default) missing.Add(nameof(EmployeeOnboardingViewModel.HireDate));
        if (profile is null || string.IsNullOrWhiteSpace(profile.WorkPhone)) missing.Add(nameof(EmployeeOnboardingViewModel.WorkPhone));
        if (profile is null || string.IsNullOrWhiteSpace(profile.PreferredLanguage)) missing.Add(nameof(EmployeeOnboardingViewModel.PreferredLanguage));
        return missing;
    }

    private static HashSet<string> GetMissingSupplierFields(ApplicationUser user, SupplierProfile? profile)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(user.FullName)) missing.Add(nameof(SupplierOnboardingViewModel.FullName));
        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName)) missing.Add(nameof(SupplierOnboardingViewModel.CompanyName));
        if (profile is null || string.IsNullOrWhiteSpace(profile.TaxNumber)) missing.Add(nameof(SupplierOnboardingViewModel.TaxNumber));
        if (profile is null || string.IsNullOrWhiteSpace(profile.CommercialRegNumber)) missing.Add(nameof(SupplierOnboardingViewModel.CommercialRegNumber));
        if (profile is null || string.IsNullOrWhiteSpace(profile.Country)) missing.Add(nameof(SupplierOnboardingViewModel.Country));
        if (profile is null || string.IsNullOrWhiteSpace(profile.City)) missing.Add(nameof(SupplierOnboardingViewModel.City));
        if (profile is null || string.IsNullOrWhiteSpace(profile.Address)) missing.Add(nameof(SupplierOnboardingViewModel.Address));
        if (profile is null || string.IsNullOrWhiteSpace(profile.ContactPerson)) missing.Add(nameof(SupplierOnboardingViewModel.ContactPerson));
        if (profile is null || string.IsNullOrWhiteSpace(profile.ContactPhone)) missing.Add(nameof(SupplierOnboardingViewModel.ContactPhone));
        if (profile is null || string.IsNullOrWhiteSpace(profile.Category)) missing.Add(nameof(SupplierOnboardingViewModel.Category));
        return missing;
    }

    private async Task<bool> RequiresInitialPasswordChangeAsync(ApplicationUser user)
    {
        if (!_environment.IsDevelopment() || string.IsNullOrWhiteSpace(user.Email) || !DemoEmployeeEmails.Contains(user.Email)
            || await IsSupplierAsync(user))
            return false;

        var claims = await _users.GetClaimsAsync(user);
        return !claims.Any(claim => claim.Type == InitialPasswordChangedClaim);
    }

}

public sealed class OnboardingFeatureModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<OnboardingGateFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<OnboardingGateFilter>());
    }
}

public sealed class OnboardingGateFilter : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _users;

    public OnboardingGateFilter(UserManager<ApplicationUser> users) => _users = users;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor descriptor
            && (descriptor.ControllerName == "Onboarding" || descriptor.ControllerName == "Account" || descriptor.ControllerName == "Home"
            || descriptor.ControllerName == "Passkey" || descriptor.ControllerName == "Face"))
        {
            await next();
            return;
        }

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var user = await _users.GetUserAsync(context.HttpContext.User);
        if (user is null || !user.MustChangePassword && user.OnboardingStatus == "Completed")
        {
            await next();
            return;
        }

        context.Result = new RedirectToActionResult("Index", "Onboarding", new { area = "Shared" });
    }
}
