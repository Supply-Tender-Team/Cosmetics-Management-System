using System.Data;
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

        var employees = _db.Users.AsNoTracking().Where(user => user.UserType == "Employee");
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
        foreach (var user in model.Users)
            user.Roles = string.Join(", ", assignments.Where(item => item.UserId == user.Id)
                .Select(item => item.Name).Where(name => !string.IsNullOrEmpty(name)));

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreateUserViewModel();
        await PopulateCreateOptionsAsync(model);
        return View(model);
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
        if (string.IsNullOrWhiteSpace(model.Email)) ModelState.AddModelError(nameof(model.Email), "Email is required.");
        if (!model.DepartmentId.HasValue ||
            !await _db.Departments.AsNoTracking().AnyAsync(d => d.Id == model.DepartmentId.Value && d.IsActive))
            ModelState.AddModelError(nameof(model.DepartmentId), "Select an active department.");
        if (string.IsNullOrWhiteSpace(model.Role) || !EmployeeRoles.Contains(model.Role) ||
            !await _roleManager.RoleExistsAsync(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Select an available employee role.");
        if (!string.IsNullOrWhiteSpace(model.Email) && await _userManager.FindByEmailAsync(model.Email) is not null)
            ModelState.AddModelError(nameof(model.Email), "This email is already in use.");

        if (ModelState.IsValid)
        {
            // Creates the account with a temporary password, emails it, audits it. See TASKS.md (AUTH-03).
            var (result, _) = await _passwords.CreateEmployeeAsync(model.FullName, model.Email, model.Role, model.DepartmentId, model.JobTitle, model.PhoneNumber);
            if (result.Succeeded)
            {
                TempData["UserSuccess"] = "Employee created. A temporary password was emailed to " + model.Email + ".";
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }

        await PopulateCreateOptionsAsync(model);
        return View(model);
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
        return View(model);
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
            return View(model);
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
                return View(model);
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
                return View(model);
            }

            if (!currentRoles.Contains(model.Role))
            {
                var addRoleResult = await _userManager.AddToRoleAsync(user, model.Role);
                if (!addRoleResult.Succeeded)
                {
                    AddIdentityErrors(addRoleResult);
                    await PopulateEditOptionsAsync(model, user.DepartmentId);
                    return View(model);
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
                    return View(model);
                }
            }

            await transaction.CommitAsync();
            await _audit.LogAsync("Updated", "ApplicationUser", user.Id, "Role: " + model.Role, string.Join("|", currentRoles));
            TempData["UserSuccess"] = "Employee updated.";
            return RedirectToAction(nameof(Index));
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
        return View(model);
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
