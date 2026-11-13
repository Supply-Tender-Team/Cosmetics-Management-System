using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class DepartmentsController : Controller
{
    // OWNER: Dev 3
    private readonly ApplicationDbContext _db;

    public DepartmentsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search, string status = "All")
        => View(await BuildPageAsync(search, status));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([Bind(Prefix = "Form")] DepartmentFormViewModel form, string? search, string status = "All")
    {
        Department? department = null;
        if (form.Id != 0)
        {
            department = await _db.Departments.FindAsync(form.Id);
            if (department is null) return NotFound();
        }

        form.Name = form.Name?.Trim() ?? "";
        form.Code = form.Code?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(form.Name)) ModelState.AddModelError("Form.Name", "Name is required.");
        if (string.IsNullOrWhiteSpace(form.Code)) ModelState.AddModelError("Form.Code", "Code is required.");

        if (ModelState.IsValid)
        {
            if (await _db.Departments.AnyAsync(d => d.Id != form.Id && d.Name == form.Name))
                ModelState.AddModelError("Form.Name", "This name is already in use.");
            if (await _db.Departments.AnyAsync(d => d.Id != form.Id && d.Code == form.Code))
                ModelState.AddModelError("Form.Code", "This code is already in use.");
        }

        if (!ModelState.IsValid)
        {
            var page = await BuildPageAsync(search, status);
            page.Form = form;
            page.OpenForm = true;
            return View("Index", page);
        }

        if (department is null)
        {
            department = new Department { Name = form.Name, Code = form.Code };
            _db.Departments.Add(department);
        }
        else
        {
            department.Name = form.Name;
            department.Code = form.Code;
        }

        try
        {
            await _db.SaveChangesAsync();
            TempData["DepartmentSuccess"] = form.Id == 0 ? "Department added." : "Department updated.";
            return RedirectToAction(nameof(Index), new { search, status });
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "Could not save the department. Check that its name and code are unique.");
            var page = await BuildPageAsync(search, status);
            page.Form = form;
            page.OpenForm = true;
            return View("Index", page);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, string? search, string status = "All")
    {
        var department = await _db.Departments.FindAsync(id);
        if (department is null) return NotFound();

        department.IsActive = isActive;
        try
        {
            await _db.SaveChangesAsync();
            TempData["DepartmentSuccess"] = isActive ? "Department reactivated." : "Department deactivated.";
        }
        catch (DbUpdateException)
        {
            TempData["DepartmentError"] = "Could not update the department status.";
        }
        return RedirectToAction(nameof(Index), new { search, status });
    }

    private async Task<DepartmentsPageViewModel> BuildPageAsync(string? search, string status)
    {
        search = search?.Trim();
        if (status is not ("Active" or "Inactive")) status = "All";

        var query = _db.Departments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => d.Name.Contains(search) || d.Code.Contains(search));
        if (status == "Active") query = query.Where(d => d.IsActive);
        if (status == "Inactive") query = query.Where(d => !d.IsActive);

        return new DepartmentsPageViewModel
        {
            Search = search,
            Status = status,
            Departments = await query.OrderBy(d => d.Name)
                .Select(d => new DepartmentRowViewModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Code = d.Code,
                    IsActive = d.IsActive,
                    MembersCount = d.AspNetUsers.Count
                }).ToListAsync()
        };
    }
}
