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

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class OnboardingController : Controller
{
    private const string InitialPasswordChangedClaim = "onboarding:initial-password-changed";
    private const string OnboardingStepClaim = "onboarding:step";
    private const string FingerprintStep = "Fingerprint";
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

    public OnboardingController(UserManager<ApplicationUser> users, ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _users = users;
        _db = db;
        _environment = environment;
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
            FaceStep => RedirectToAction(nameof(Face)),
            FinishStep => RedirectToAction(nameof(Complete)),
            _ => RedirectToAction(nameof(Fingerprint)),
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

        var result = await _users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
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
        var stepUpdate = await SetOnboardingStepAsync(user, FingerprintStep);
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
        var profile = await _db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        var model = profile is null ? new EmployeeOnboardingViewModel { FullName = user.FullName } : new EmployeeOnboardingViewModel
        {
            FullName = user.FullName,
            EmployeeNumber = profile.EmployeeNumber,
            HireDate = profile.HireDate,
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
        var stepUpdate = await SetOnboardingStepAsync(user, FingerprintStep);
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
        var stepUpdate = await SetOnboardingStepAsync(user, FingerprintStep);
        if (!stepUpdate.Succeeded)
        {
            foreach (var error in stepUpdate.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Fingerprint(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (user.MustChangePassword || await RequiresInitialPasswordChangeAsync(user)
            || await IsAtStepAsync(user, FaceStep, FinishStep) || await HasMissingProfileFieldsAsync(user, cancellationToken))
            return RedirectToAction(nameof(Index));
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Fingerprint(bool continueDemo, CancellationToken cancellationToken)
    {
        if (!continueDemo) return View();
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.MustChangePassword || await RequiresInitialPasswordChangeAsync(user)
            || await HasMissingProfileFieldsAsync(user, cancellationToken))
            return RedirectToAction(nameof(Index));
        var result = await SetOnboardingStepAsync(user, FaceStep);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }
        return RedirectToAction(nameof(Face));
    }

    [HttpGet]
    public async Task<IActionResult> Face(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (user.MustChangePassword || await RequiresInitialPasswordChangeAsync(user)
            || !await IsAtStepAsync(user, FaceStep) || await HasMissingProfileFieldsAsync(user, cancellationToken))
            return RedirectToAction(nameof(Index));
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Face(bool continueDemo, CancellationToken cancellationToken)
    {
        if (!continueDemo) return View();
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.MustChangePassword || !await IsAtStepAsync(user, FaceStep)
            || await HasMissingProfileFieldsAsync(user, cancellationToken))
            return RedirectToAction(nameof(Index));
        var result = await SetOnboardingStepAsync(user, FinishStep);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }
        return RedirectToAction(nameof(Complete));
    }

    [HttpGet]
    public async Task<IActionResult> Complete()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (user.MustChangePassword || !await IsAtStepAsync(user, FinishStep) || await HasMissingProfileFieldsAsync(user, HttpContext.RequestAborted))
            return RedirectToAction(nameof(Index));
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
        user.BiometricConsentAt = DateTime.UtcNow;
        user.OnboardingStatus = "Completed";
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        await ClearOnboardingStepAsync(user);
        return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
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
        return claim?.Value ?? FingerprintStep;
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
            && (descriptor.ControllerName == "Onboarding" || descriptor.ControllerName == "Account" || descriptor.ControllerName == "Home"))
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
