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
            if (supplier is null || string.IsNullOrWhiteSpace(user.FullName)
                || string.IsNullOrWhiteSpace(supplier.CompanyName) || string.IsNullOrWhiteSpace(supplier.TaxNumber)
                || string.IsNullOrWhiteSpace(supplier.CommercialRegNumber) || string.IsNullOrWhiteSpace(supplier.Country)
                || string.IsNullOrWhiteSpace(supplier.City) || string.IsNullOrWhiteSpace(supplier.Address)
                || string.IsNullOrWhiteSpace(supplier.ContactPerson) || string.IsNullOrWhiteSpace(supplier.ContactPhone)
                || string.IsNullOrWhiteSpace(supplier.Category))
                return RedirectToAction(nameof(Supplier));
        }
        else
        {
            var employee = await _db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            if (employee is null || string.IsNullOrWhiteSpace(user.FullName)
                || string.IsNullOrWhiteSpace(employee.EmployeeNumber) || employee.HireDate == default
                || string.IsNullOrWhiteSpace(employee.WorkPhone) || string.IsNullOrWhiteSpace(employee.PreferredLanguage))
                return RedirectToAction(nameof(Profile));
        }

        return RedirectToAction(nameof(Fingerprint));
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
        await _users.UpdateAsync(user);
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
        return View(profile is null ? new EmployeeOnboardingViewModel { FullName = user.FullName } : new EmployeeOnboardingViewModel
        {
            FullName = user.FullName,
            EmployeeNumber = profile.EmployeeNumber,
            HireDate = profile.HireDate,
            WorkPhone = profile.WorkPhone,
            PreferredLanguage = profile.PreferredLanguage
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(EmployeeOnboardingViewModel model, CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await IsSupplierAsync(user)) return Forbid();
        ViewBag.RoleLabel = Roles.LabelFor(User);
        if (!ModelState.IsValid) return View(model);
        if (await _db.EmployeeProfiles.AnyAsync(x => x.EmployeeNumber == model.EmployeeNumber && x.UserId != user.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.EmployeeNumber), "This employee number is already used.");
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.OnboardingStatus = "InProgress";
        var userResult = await _users.UpdateAsync(user);
        if (!userResult.Succeeded)
        {
            foreach (var error in userResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var profile = await _db.EmployeeProfiles.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        if (profile is null)
        {
            profile = new EmployeeProfile { UserId = user.Id };
            _db.EmployeeProfiles.Add(profile);
        }
        profile.EmployeeNumber = model.EmployeeNumber.Trim();
        profile.HireDate = model.HireDate;
        profile.WorkPhone = model.WorkPhone.Trim();
        profile.PreferredLanguage = model.PreferredLanguage;
        await _db.SaveChangesAsync(cancellationToken);
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
        return View(profile is null ? new SupplierOnboardingViewModel { FullName = user.FullName, ContactPerson = user.FullName } : new SupplierOnboardingViewModel
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
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Supplier(SupplierOnboardingViewModel model, CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await IsSupplierAsync(user)) return Forbid();
        ViewBag.Categories = RegisterSupplierViewModel.Categories;
        if (!ModelState.IsValid) return View(model);
        if (await _db.SupplierProfiles.AnyAsync(x => x.TaxNumber == model.TaxNumber && x.UserId != user.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.TaxNumber), "This tax number is already registered.");
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.OnboardingStatus = "InProgress";
        var userResult = await _users.UpdateAsync(user);
        if (!userResult.Succeeded)
        {
            foreach (var error in userResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var profile = await _db.SupplierProfiles.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        if (profile is null)
        {
            profile = new SupplierProfile { UserId = user.Id, Status = "Pending" };
            _db.SupplierProfiles.Add(profile);
        }
        profile.CompanyName = model.CompanyName.Trim();
        profile.TaxNumber = model.TaxNumber.Trim();
        profile.CommercialRegNumber = model.CommercialRegNumber.Trim();
        profile.Country = model.Country.Trim();
        profile.City = model.City.Trim();
        profile.Address = model.Address.Trim();
        profile.ContactPerson = model.ContactPerson.Trim();
        profile.ContactPhone = model.ContactPhone.Trim();
        profile.Website = string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim();
        profile.Category = model.Category.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Fingerprint(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Fingerprint(bool continueDemo)
    {
        if (!continueDemo) return View();
        TempData["OnboardingFingerprintPreviewed"] = true;
        return RedirectToAction(nameof(Face));
    }

    [HttpGet]
    public async Task<IActionResult> Face(CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (TempData.Peek("OnboardingFingerprintPreviewed") is not true) return RedirectToAction(nameof(Index));
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Face(bool continueDemo)
    {
        if (!continueDemo) return View();
        TempData["OnboardingFacePreviewed"] = true;
        return RedirectToAction(nameof(Complete));
    }

    [HttpGet]
    public async Task<IActionResult> Complete()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.OnboardingStatus == "Completed") return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
        if (TempData.Peek("OnboardingFacePreviewed") is not true) return RedirectToAction(nameof(Index));
        return View(new OnboardingConsentViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(OnboardingConsentViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        user.BiometricConsentAt = DateTime.UtcNow;
        user.OnboardingStatus = "Completed";
        await _users.UpdateAsync(user);
        return Redirect(DashboardRouter.UrlFor(await _users.GetRolesAsync(user)));
    }

    private async Task<bool> IsSupplierAsync(ApplicationUser user) =>
        user.UserType.Equals("Supplier", StringComparison.OrdinalIgnoreCase)
        || await _users.IsInRoleAsync(user, Roles.Supplier);

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
