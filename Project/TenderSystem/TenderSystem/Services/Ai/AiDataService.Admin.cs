using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// ADMIN: the whole system's administration data (the same things the Admin pages show). Never passwords, tokens, biometrics or IP addresses.
public sealed partial class AiDataService
{
    private async Task<AiDataResult?> AdminAsync(string q, string question, CancellationToken ct)
    {
        if (Wants(q, "audit", "تدقيق", "سجل", "activity"))
        {
            var rows = await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Take(Count(question))
                .Select(a => new { a.CreatedAt, User = a.User != null ? a.User.FullName : "system", a.Action, a.EntityName, a.EntityId, a.NewValues })
                .ToListAsync(ct);

            var labels = await AuditLabelsAsync(rows.Select(r => (r.EntityName, r.EntityId)), ct);
            return Make("AuditLogs", new AiTable("Latest audit log records", ["Time (UTC)", "User", "Action", "Record", "Details"],
                rows.Select(r => new[] { Time(r.CreatedAt), Cut(r.User, 40), r.Action, RecordLabel(labels, r.EntityName, r.EntityId), Cut(r.NewValues, 80) }).ToList()));
        }

        if (Wants(q, "supplier", "مورد"))
        {
            if (Wants(q, "pending", "waiting", "approv", "review", "معلق", "انتظار", "موافق"))
            {
                var rows = await _db.SupplierProfiles.AsNoTracking()
                    .Where(s => s.Status == Statuses.SupplierStatus.Pending)
                    .OrderByDescending(s => s.CreatedAt).Take(Count(question))
                    .Select(s => new { s.CompanyName, s.Category, s.Country, s.City, s.CreatedAt })
                    .ToListAsync(ct);
                return Make("PendingSuppliers", new AiTable("Suppliers waiting for approval", ["Company", "Category", "Country", "City", "Registered"],
                    rows.Select(r => new[] { Cut(r.CompanyName, 60), r.Category, r.Country, r.City, Day(r.CreatedAt) }).ToList()));
            }

            var byStatus = await _db.SupplierProfiles.AsNoTracking()
                .GroupBy(s => s.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            return Make("Suppliers", new AiTable("Suppliers by status", ["Status", "Suppliers"],
                byStatus.Select(r => new[] { r.Status, r.Count.ToString() }).ToList()));
        }

        // a word like "employee" alone is not a request for numbers: it must ask for a count or a list
        if (Wants(q, "user", "employee", "staff", "مستخدم", "موظف") && AsksForCountOrList(q))
        {
            var byRole = await (from ur in _db.UserRoles
                                join r in _db.Roles on ur.RoleId equals r.Id
                                group r by r.Name into g
                                select new { Role = g.Key, Count = g.Count() }).ToListAsync(ct);
            return Make("UsersByRole", new AiTable("Users by role", ["Role", "Users"],
                byRole.OrderBy(r => r.Role).Select(r => new[] { Roles.LabelFor(r.Role ?? ""), r.Count.ToString() }).ToList()));
        }

        if (Wants(q, "contact", "message", "رسال"))
        {
            var rows = await _db.ContactMessages.AsNoTracking()
                .OrderBy(m => m.IsRead).ThenByDescending(m => m.CreatedAt).Take(Count(question))
                .Select(m => new { m.CreatedAt, m.FullName, m.Subject, m.IsRead })
                .ToListAsync(ct);
            return Make("ContactMessages", new AiTable("Contact messages (unread first)", ["Received (UTC)", "From", "Subject", "Status"],
                rows.Select(r => new[] { Time(r.CreatedAt), Cut(r.FullName, 40), Cut(r.Subject, 60), r.IsRead ? "Read" : "Unread" }).ToList()));
        }

        return null;
    }
}
