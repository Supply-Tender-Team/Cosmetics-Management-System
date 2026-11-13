using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class DashboardController : Controller
{
    // OWNER: Dev 3
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db) => _db = db;

    // Read-only: this page only counts rows, it never changes data.
    public async Task<IActionResult> Index()
    {
        var employees = _db.Users.AsNoTracking().Where(u => u.UserType == "Employee");
        var departments = _db.Departments.AsNoTracking();
        var suppliers = _db.SupplierProfiles.AsNoTracking();
        var tenders = _db.Tenders.AsNoTracking();

        var model = new AdminDashboardViewModel
        {
            EmployeesTotal = await employees.CountAsync(),
            EmployeesActive = await employees.CountAsync(u => u.IsActive),
            DepartmentsTotal = await departments.CountAsync(),
            DepartmentsActive = await departments.CountAsync(d => d.IsActive),
            SuppliersApproved = await suppliers.CountAsync(s => s.Status == "Approved"),
            SuppliersPending = await suppliers.CountAsync(s => s.Status == "Pending"),
            TendersTotal = await tenders.CountAsync(),
            TendersOpen = await tenders.CountAsync(t => t.Status == "Published" || t.Status == "Open"),

            EmployeesByRole = await (from userRole in _db.UserRoles.AsNoTracking()
                                     join user in employees on userRole.UserId equals user.Id
                                     join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                     group role by new { role.Id, role.Name } into g
                                     orderby g.Count() descending
                                     select new DashboardBarViewModel { Label = g.Key.Name ?? "", Count = g.Count(), FilterValue = g.Key.Id })
                                    .ToListAsync(),

            EmployeesByDepartment = await departments
                .Select(d => new DashboardBarViewModel
                {
                    Label = d.Name,
                    Count = d.AspNetUsers.Count(u => u.UserType == "Employee"),
                    FilterValue = d.Id.ToString()
                })
                .Where(b => b.Count > 0)
                .OrderByDescending(b => b.Count).ThenBy(b => b.Label)
                .Take(6)
                .ToListAsync(),

            SuppliersByStatus = await suppliers
                .GroupBy(s => s.Status)
                .Select(g => new DashboardBarViewModel { Label = g.Key, Count = g.Count() })
                .OrderByDescending(b => b.Count)
                .ToListAsync()
        };

        // Show friendly role names (e.g. "Sourcing Officer") instead of the internal role keys.
        foreach (var bar in model.EmployeesByRole) bar.Label = Roles.LabelFor(bar.Label);

        return View(model);
    }
}
