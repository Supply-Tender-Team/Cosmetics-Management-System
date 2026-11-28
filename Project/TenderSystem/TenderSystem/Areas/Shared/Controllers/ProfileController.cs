using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Biometrics;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class ProfileController : Controller
{
    // OWNER: Sara

    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ApplicationDbContext _db;
    private readonly PasskeyService _passkeys;
    private readonly FaceService _face;
    private readonly AuditService _audit;

    public ProfileController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        ApplicationDbContext db, PasskeyService passkeys, FaceService face, AuditService audit)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _passkeys = passkeys;
        _face = face;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Challenge();
        await _db.Entry(user).Reference(x => x.Department).LoadAsync(cancellationToken);
        var employee = await _db.EmployeeProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var supplier = await _db.SupplierProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var roles = await _users.GetRolesAsync(user);

        var model = new ProfileViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = employee?.WorkPhone ?? user.PhoneNumber ?? string.Empty,
            Role = string.Join(", ", roles.Select(Roles.LabelFor)),
            AccountType = string.Equals(user.UserType, "Supplier", StringComparison.OrdinalIgnoreCase) ? "Supplier" : "Employee",
            Department = user.Department?.Name ?? string.Empty,
            JobTitle = user.JobTitle ?? string.Empty,
            EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
            CompanyName = supplier?.CompanyName ?? string.Empty
        };

        ViewData["Devices"] = await _passkeys.ListAsync(userId);
        ViewData["Faces"] = await _face.CountAsync(userId);
        ViewData["FaceList"] = await _face.ListAsync(userId);
        ViewData["Reload"] = true;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Challenge();

        var employee = string.Equals(user.UserType, "Supplier", StringComparison.OrdinalIgnoreCase)
            ? null
            : await _db.EmployeeProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        return View(new EditMyProfileViewModel { FullName = user.FullName, PhoneNumber = employee?.WorkPhone ?? user.PhoneNumber });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditMyProfileViewModel model)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Challenge();
        if (!ModelState.IsValid) return View(model);

        user.FullName = model.FullName.Trim();
        var phone = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        var employee = string.Equals(user.UserType, "Supplier", StringComparison.OrdinalIgnoreCase)
            ? null
            : await _db.EmployeeProfiles.FirstOrDefaultAsync(x => x.UserId == userId);
        if (employee is null) user.PhoneNumber = phone;
        else employee.WorkPhone = phone ?? string.Empty;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        if (employee is not null) await _db.SaveChangesAsync();

        await _audit.LogAsync("Updated", "ApplicationUser", user.Id, "Profile information updated.");
        await _signIn.RefreshSignInAsync(user);
        TempData["ProfileSaved"] = "Your profile information has been updated.";
        return RedirectToAction(nameof(Index));
    }
}
