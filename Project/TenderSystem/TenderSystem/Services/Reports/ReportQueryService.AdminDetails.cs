using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private async Task FillAdminDetailsAsync(string type, List<ReportRecordViewModel> rows, List<int> ids, CancellationToken ct)
    {
        if (type == "users")
        {
            var userIds = rows.Select(r => r.RecordId).ToList();
            var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserType, u.EmailConfirmed, u.LastLoginAt }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var user = users.Single(u => u.Id == row.RecordId);
                AddDetail(row, "Account type", user.UserType);
                AddDetail(row, "Email confirmed", user.EmailConfirmed ? "Yes" : "No");
                AddDetail(row, "Last sign-in", DateText(user.LastLoginAt));
            }
        }
        else if (type == "suppliers")
        {
            var suppliers = await _db.SupplierProfiles.AsNoTracking().Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.ContactPerson, s.ContactPhone, s.User.Email, s.User.IsActive }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var supplier = suppliers.Single(s => s.Id.ToString() == row.RecordId);
                AddDetail(row, "Contact person", supplier.ContactPerson);
                AddDetail(row, "Contact phone", supplier.ContactPhone);
                AddDetail(row, "Account email", supplier.Email);
                AddDetail(row, "Account active", supplier.IsActive ? "Yes" : "No");
            }
        }
        else if (type == "departments")
        {
            var departments = await _db.Departments.AsNoTracking().Where(d => ids.Contains(d.Id))
                .Select(d => new { d.Id, Total = d.AspNetUsers.Count(), Active = d.AspNetUsers.Count(u => u.IsActive) }).ToListAsync(ct);
            var roleCounts = await _db.UserRoles.AsNoTracking().Join(_db.Users.AsNoTracking().Where(u => u.DepartmentId != null && ids.Contains(u.DepartmentId.Value)),
                membership => membership.UserId, user => user.Id, (membership, user) => new { membership.RoleId, user.DepartmentId })
                .Join(_db.Roles.AsNoTracking(), membership => membership.RoleId, role => role.Id,
                    (membership, role) => new { membership.DepartmentId, role.Name })
                .GroupBy(r => new { r.DepartmentId, r.Name }).Select(g => new { g.Key.DepartmentId, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var department = departments.Single(d => d.Id.ToString() == row.RecordId);
                AddDetail(row, "Total accounts", department.Total.ToString());
                AddDetail(row, "Active / inactive", department.Active + " / " + (department.Total - department.Active));
                var roles = string.Join(", ", roleCounts.Where(r => r.DepartmentId == department.Id)
                    .Select(r => TenderSystem.Infrastructure.Roles.LabelFor(r.Name ?? "") + ": " + r.Count));
                AddDetail(row, "Roles in this department", roles.Length == 0 ? "No accounts assigned" : roles);
            }
        }
        else if (type == "audit")
        {
            var days = rows.Where(r => r.OccurredAt.HasValue).Select(r => r.OccurredAt!.Value.Date).ToList();
            var start = days.Min(); var end = days.Max().AddDays(1); var actions = rows.Select(r => r.Reference).Distinct().ToList();
            var totals = await _db.AuditLogs.AsNoTracking().Where(a => a.CreatedAt >= start && a.CreatedAt < end && actions.Contains(a.Action))
                .GroupBy(a => new { Day = a.CreatedAt.Date, a.Action }).Select(g => new { g.Key.Day, g.Key.Action, Events = g.Count(),
                    Accounts = g.Where(a => a.UserId != null).Select(a => a.UserId).Distinct().Count(), Anonymous = g.Count(a => a.UserId == null) }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var total = totals.Single(t => t.Day == row.OccurredAt?.Date && t.Action == row.Reference);
                AddDetail(row, "Recorded events", total.Events.ToString());
                AddDetail(row, "Identified accounts", total.Accounts.ToString());
                AddDetail(row, "Without an identified account", total.Anonymous.ToString());
            }
        }
        else if (type == "sign-ins")
        {
            var days = rows.Where(r => r.OccurredAt.HasValue).Select(r => r.OccurredAt!.Value.Date).ToList();
            var start = days.Min(); var end = days.Max().AddDays(1); var methods = rows.Select(r => r.Reference).Distinct().ToList();
            var totals = await _db.BiometricAuthLogs.AsNoTracking().Where(a => a.CreatedAt >= start && a.CreatedAt < end && methods.Contains(a.Method))
                .GroupBy(a => new { Day = a.CreatedAt.Date, a.Method, a.Success }).Select(g => new { g.Key.Day, g.Key.Method, g.Key.Success,
                    Attempts = g.Count(), First = g.Min(a => a.CreatedAt), Last = g.Max(a => a.CreatedAt) }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var total = totals.Single(t => t.Day == row.OccurredAt?.Date && t.Method == row.Reference && t.Success == (row.Status == "Succeeded"));
                AddDetail(row, "Attempts", total.Attempts.ToString());
                AddDetail(row, "First recorded attempt", DateText(total.First));
                AddDetail(row, "Last recorded attempt", DateText(total.Last));
            }
        }
    }
}
