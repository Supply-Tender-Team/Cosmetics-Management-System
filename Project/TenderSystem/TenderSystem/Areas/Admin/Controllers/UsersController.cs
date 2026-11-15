using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Auth;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class UsersController : Controller
{
    // OWNER: Dev 3
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly PasswordService _passwords;
    private readonly AuditService _audit;
    private static readonly string[] EmployeeRoles =
        [Roles.Requester, Roles.SourcingOfficer, Roles.EvaluationCommittee, Roles.SourcingManager, Roles.TopCommittee];

    public UsersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, PasswordService passwords, AuditService audit)
    {
        _passwords = passwords;
        _audit = audit;
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IActionResult> Index(string? search, int? departmentId, string? roleId, string status = "All")
    {
        search = search?.Trim();
        if (status is not ("Active" or "Inactive")) status = "All";

        var employees = _db.Users.AsNoTracking().Where(user => user.UserType == "Employee" &&
            !_db.UserRoles.Any(userRole => userRole.UserId == user.Id &&
                _db.Roles.Any(role => role.Id == userRole.RoleId && role.Name == Roles.Admin)));
        var model = new UsersPageViewModel
        {
            Search = search,
            DepartmentId = departmentId,
            RoleId = roleId,
            Status = status,
            TotalCount = await employees.CountAsync(),
            ActiveCount = await employees.CountAsync(user => user.IsActive),
            Departments = await _db.Departments.AsNoTracking()
                .OrderBy(department => department.Name)
                .Select(department => new UserDepartmentOption { Id = department.Id, Name = department.Name })
                .ToListAsync(),
            Roles = await (from userRole in _db.UserRoles.AsNoTracking()
                           join user in employees on userRole.UserId equals user.Id
                           join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                           select new UserRoleOption { Id = role.Id, Name = role.Name ?? "" })
                .Distinct().OrderBy(role => role.Name).ToListAsync()
        };
        model.InactiveCount = model.TotalCount - model.ActiveCount;

        if (!string.IsNullOrWhiteSpace(search))
            employees = employees.Where(user => user.FullName.Contains(search) || (user.Email != null && user.Email.Contains(search)));
        if (departmentId.HasValue)
            employees = employees.Where(user => user.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(roleId))
            employees = employees.Where(user => _db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == roleId));
        if (status == "Active") employees = employees.Where(user => user.IsActive);
        if (status == "Inactive") employees = employees.Where(user => !user.IsActive);

        model.Users = await employees.OrderByDescending(user => user.CreatedAt).ThenByDescending(user => user.Id)
            .Select(user => new UserRowViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? "",
                Department = user.Department != null ? user.Department.Name : null,
                JobTitle = user.JobTitle,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            }).ToListAsync();

        var userIds = model.Users.Select(user => user.Id).ToList();
        var assignments = await (from userRole in _db.UserRoles.AsNoTracking()
                                 join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                 where userIds.Contains(userRole.UserId)
                                 select new { userRole.UserId, role.Name }).ToListAsync();
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        foreach (var user in model.Users)
        {
            var roleNames = assignments.Where(item => item.UserId == user.Id)
                .Select(item => item.Name).OfType<string>().ToArray();
            user.Roles = string.Join(", ", roleNames);
            user.CanManage = user.Id != currentUserId && roleNames.Length > 0 &&
                roleNames.All(role => EmployeeRoles.Contains(role));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Disable(string id, bool? expectedIsActive, string? search, int? departmentId, string? roleId, string status = "All")
        => SetActiveAsync(id, false, expectedIsActive, search, departmentId, roleId, status);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Enable(string id, bool? expectedIsActive, string? search, int? departmentId, string? roleId, string status = "All")
        => SetActiveAsync(id, true, expectedIsActive, search, departmentId, roleId, status);

    private async Task<IActionResult> SetActiveAsync(string id, bool isActive, bool? expectedIsActive,
        string? search, int? departmentId, string? roleId, string status)
    {
        if (string.IsNullOrWhiteSpace(id) || !expectedIsActive.HasValue) return BadRequest();
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var user = await FindManageableEmployeeAsync(id);
            if (user is null) return NotFound();
            if (user.IsActive != expectedIsActive.Value || user.IsActive == isActive)
            {
                TempData["UserError"] = "This employee's status changed. Refresh the list and try again.";
                return RedirectToAction(nameof(Index), new { search, departmentId, roleId, status });
            }

            user.IsActive = isActive;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                TempData["UserError"] = "The employee changed while you were updating the account. Please try again.";
                return RedirectToAction(nameof(Index), new { search, departmentId, roleId, status });
            }
            if (!isActive)
            {
                result = await _userManager.UpdateSecurityStampAsync(user);
                if (!result.Succeeded)
                {
                    TempData["UserError"] = "Could not disable the employee. Please try again.";
                    return RedirectToAction(nameof(Index), new { search, departmentId, roleId, status });
                }
            }

            await transaction.CommitAsync();
            TempData["UserSuccess"] = isActive ? "Employee enabled." : "Employee disabled.";
        }
        catch (Exception)
        {
            TempData["UserError"] = "Could not update the employee. Please try again.";
        }
        return RedirectToAction(nameof(Index), new { search, departmentId, roleId, status });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPasswordReset(string id, string? search, int? departmentId, string? roleId, string status = "All")
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var user = await FindManageableEmployeeAsync(id);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(user.Email)) return BadRequest();

        try
        {
            await _passwords.SendResetLinkAsync(user.Email, (email, token) =>
                Url.Action("ResetPassword", "Account", new { area = "", email, token })
                ?? throw new InvalidOperationException("Reset route is unavailable."));
            TempData["UserSuccess"] = "Password reset requested.";
        }
        catch (Exception)
        {
            TempData["UserError"] = "Could not request a password reset. Please try again.";
        }
        return RedirectToAction(nameof(Index), new { search, departmentId, roleId, status });
    }

    private async Task<ApplicationUser?> FindManageableEmployeeAsync(string id)
    {
        if (id == User.FindFirstValue(ClaimTypes.NameIdentifier)) return null;
        var user = await _userManager.FindByIdAsync(id);
        if (user is null || user.UserType != "Employee") return null;
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Count > 0 && roles.All(role => EmployeeRoles.Contains(role)) ? user : null;
    }

    // ---- Modal support -------------------------------------------------------------
    // The Users list opens Add / Edit in a Bootstrap modal (wwwroot/js/admin-modal.js) and sends
    // "X-Requested-With: XMLHttpRequest". Those requests get only the form (a partial view);
    // normal requests still get the full page, so direct URLs keep working without JavaScript.
    private bool IsModalRequest() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var user = await _db.Users.AsNoTracking()
            .Where(item => item.Id == id && item.UserType == "Employee")
            .Select(item => new UserDetailsViewModel
            {
                Id = item.Id,
                FullName = item.FullName,
                Email = item.Email ?? "",
                PhoneNumber = item.PhoneNumber,
                Department = item.Department != null ? item.Department.Name : null,
                IsActive = item.IsActive,
                CreatedAt = item.CreatedAt
            }).SingleOrDefaultAsync();
        if (user is null) return NotFound();

        var roles = await (from assignment in _db.UserRoles.AsNoTracking()
                           join role in _db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                           where assignment.UserId == id
                           select role.Name).ToListAsync();
        if (roles.Contains(Roles.Admin)) return NotFound();
        user.Roles = string.Join(", ", roles.OfType<string>());
        user.RoleLabels = string.Join(", ", roles.OfType<string>().Select(Roles.LabelFor));
        return FormResult("_Details", user);
    }

    private IActionResult FormResult(string partialName, object model) =>
        IsModalRequest() ? PartialView(partialName, model) : View(model);

    // After a successful save: the message is shown on the Users list (TempData). The modal
    // gets a small JSON answer, closes itself and reloads the list with the current filters.
    private IActionResult SavedResult(string message)
    {
        TempData["UserSuccess"] = message;
        return IsModalRequest() ? Json(new { saved = true }) : RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreateUserViewModel();
        await PopulateCreateOptionsAsync(model);
        return FormResult("_CreateForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        model.FullName = model.FullName?.Trim() ?? "";
        model.Email = model.Email?.Trim() ?? "";
        model.JobTitle = model.JobTitle?.Trim();
        model.PhoneNumber = model.PhoneNumber?.Trim();

        if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError(nameof(model.FullName), "Full name is required.");
        else if (model.FullName.Length < 2 || model.FullName.Length > 150)
            ModelState.AddModelError(nameof(model.FullName), "Full name must be between 2 and 150 characters.");
        if (string.IsNullOrWhiteSpace(model.Email)) ModelState.AddModelError(nameof(model.Email), "Email is required.");
        var departmentName = model.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking()
                .Where(d => d.Id == model.DepartmentId.Value && d.IsActive)
                .Select(d => d.Name).SingleOrDefaultAsync()
            : null;
        if (departmentName is null)
            ModelState.AddModelError(nameof(model.DepartmentId), "Select an active department.");
        if (string.IsNullOrWhiteSpace(model.Role) || !EmployeeRoles.Contains(model.Role) ||
            !await _roleManager.RoleExistsAsync(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Select an available employee role.");
        if (!string.IsNullOrWhiteSpace(model.Email) && await _userManager.FindByEmailAsync(model.Email) is not null)
            ModelState.AddModelError(nameof(model.Email), "This email is already in use.");

        if (ModelState.IsValid)
        {
            // Creates the account with a temporary password, emails it, audits it. See TASKS.md (AUTH-03).
            var (result, _) = await _passwords.CreateEmployeeAsync(model.FullName, model.Email, model.Role,
                model.DepartmentId, model.JobTitle, model.PhoneNumber);
            if (result.Succeeded)
            {
                return SavedResult("Employee account created. Password setup instructions were requested; delivery is not confirmed.");
            }
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }

        await PopulateCreateOptionsAsync(model);
        return FormResult("_CreateForm", model);
    }

    private async Task PopulateCreateOptionsAsync(CreateUserViewModel model)
    {
        model.Departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new CreateDepartmentOption { Id = d.Id, Name = d.Name })
            .ToListAsync();
        model.AvailableRoles = EmployeeRoles.Select(role => new CreateRoleOption
        {
            Value = role,
            Label = Roles.LabelFor(role)
        }).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null || user.UserType != "Employee") return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Contains(Roles.Admin) || currentRoles.Contains(Roles.Supplier)) return NotFound();

        var model = new EditUserViewModel
        {
            Id = user.Id,
            ConcurrencyStamp = user.ConcurrencyStamp ?? "",
            OriginalRoles = string.Join("|", currentRoles.OrderBy(role => role)),
            Email = user.Email ?? "",
            FullName = user.FullName,
            JobTitle = user.JobTitle,
            PhoneNumber = user.PhoneNumber,
            DepartmentId = user.DepartmentId,
            Role = currentRoles.Count == 1 ? currentRoles[0] : ""
        };
        await PopulateEditOptionsAsync(model, user.DepartmentId);
        return FormResult("_EditForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        var user = await _userManager.FindByIdAsync(model.Id);
        if (user is null || user.UserType != "Employee") return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Contains(Roles.Admin) || currentRoles.Contains(Roles.Supplier)) return NotFound();

        model.Email = user.Email ?? "";
        model.FullName = model.FullName?.Trim() ?? "";
        model.JobTitle = model.JobTitle?.Trim();
        model.PhoneNumber = model.PhoneNumber?.Trim();

        if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError(nameof(model.FullName), "Full name is required.");
        if (user.ConcurrencyStamp != model.ConcurrencyStamp ||
            string.Join("|", currentRoles.OrderBy(role => role)) != model.OriginalRoles)
            ModelState.AddModelError("", "This employee changed while you were editing. Reload the page and try again.");
        if (!model.DepartmentId.HasValue ||
            !await _db.Departments.AsNoTracking().AnyAsync(d => d.Id == model.DepartmentId.Value &&
                (d.IsActive || d.Id == user.DepartmentId)))
            ModelState.AddModelError(nameof(model.DepartmentId), "Select an active department or the employee's current department.");
        if (string.IsNullOrWhiteSpace(model.Role) || !EmployeeRoles.Contains(model.Role) ||
            !await _roleManager.RoleExistsAsync(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Select an available employee role.");

        if (!ModelState.IsValid)
        {
            await PopulateEditOptionsAsync(model, user.DepartmentId);
            return FormResult("_EditForm", model);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Contains(Roles.Admin) || currentRoles.Contains(Roles.Supplier)) return NotFound();
            if (string.Join("|", currentRoles.OrderBy(role => role)) != model.OriginalRoles)
            {
                ModelState.AddModelError("", "This employee's roles changed. Reload the page and try again.");
                await PopulateEditOptionsAsync(model, user.DepartmentId);
                return FormResult("_EditForm", model);
            }

            user.FullName = model.FullName;
            user.JobTitle = model.JobTitle;
            user.PhoneNumber = model.PhoneNumber;
            user.DepartmentId = model.DepartmentId;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                AddIdentityErrors(updateResult);
                await PopulateEditOptionsAsync(model, user.DepartmentId);
                return FormResult("_EditForm", model);
            }

            if (!currentRoles.Contains(model.Role))
            {
                var addRoleResult = await _userManager.AddToRoleAsync(user, model.Role);
                if (!addRoleResult.Succeeded)
                {
                    AddIdentityErrors(addRoleResult);
                    await PopulateEditOptionsAsync(model, user.DepartmentId);
                    return FormResult("_EditForm", model);
                }
            }

            var rolesToRemove = currentRoles.Where(role => role != model.Role).ToArray();
            if (rolesToRemove.Length > 0)
            {
                var removeRoleResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeRoleResult.Succeeded)
                {
                    AddIdentityErrors(removeRoleResult);
                    await PopulateEditOptionsAsync(model, user.DepartmentId);
                    return FormResult("_EditForm", model);
                }
            }

            await transaction.CommitAsync();
            await _audit.LogAsync("Updated", "ApplicationUser", user.Id, "Role: " + model.Role, string.Join("|", currentRoles));
            return SavedResult("Employee updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError("", "This employee changed while you were editing. Reload the page and try again.");
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "Could not save the employee. Please try again.");
        }

        await PopulateEditOptionsAsync(model, user.DepartmentId);
        return FormResult("_EditForm", model);
    }

    private async Task PopulateEditOptionsAsync(EditUserViewModel model, int? currentDepartmentId)
    {
        model.Departments = await _db.Departments.AsNoTracking()
            .Where(department => department.IsActive || department.Id == currentDepartmentId)
            .OrderBy(department => department.Name)
            .Select(department => new EditDepartmentOption
            {
                Id = department.Id,
                Name = department.Name,
                IsActive = department.IsActive
            }).ToListAsync();
        model.AvailableRoles = EmployeeRoles.Select(role => new EditRoleOption
        {
            Value = role,
            Label = Roles.LabelFor(role)
        }).ToList();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);
    }
}
