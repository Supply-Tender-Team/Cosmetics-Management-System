using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Data;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    public const int ExportLimit = 5000;
    private readonly ApplicationDbContext _db;
    public ReportQueryService(ApplicationDbContext db) { _db = db; }

    public async Task<ReportPageViewModel?> ReadAsync(ClaimsPrincipal user, ReportFilterViewModel filter, bool export, CancellationToken ct)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId)) return null;
        var model = new ReportPageViewModel { Filter = filter, Options = GetOptions(user),
            GeneratedBy = user.FindFirst("FullName")?.Value ?? user.Identity.Name ?? "Signed-in user" };
        model.Selected = string.IsNullOrEmpty(filter.Type) ? model.Options.FirstOrDefault() : model.Options.FirstOrDefault(o => o.Key == filter.Type);
        if (model.Selected == null) return model.Options.Count == 0 && string.IsNullOrEmpty(filter.Type) ? model : null;
        filter.Type = model.Selected.Key;
        if (!model.Selected.HasDate) { filter.From = null; filter.To = null; }
        if (!new[] { "newest", "oldest", "reference", "status", "value" }.Contains(filter.Sort)
            || (filter.Sort == "value" && !model.Selected.HasAmount)) filter.Sort = "newest";
        if (!Validator.TryValidateObject(filter, new ValidationContext(filter), new List<ValidationResult>(), true)
            || filter.From > filter.To || filter.To?.Year >= 9999)
        { model.Error = "Check your filters. The start date must not be after the end date."; return model; }
        filter.Search = filter.Search?.Trim(); filter.TenderNumber = filter.TenderNumber?.Trim();
        var query = CreateQuery(model.Selected.Key, userId);
        // Filter choices are derived from this user's scoped records, never a global directory.
        model.Statuses = await query.Select(r => r.Status).Distinct().OrderBy(s => s).ToListAsync(ct);
        if (model.Selected.HasDepartment)
            model.Departments = (await query.Where(r => r.DepartmentId != null).Select(r => new { r.DepartmentId, r.Department }).Distinct().OrderBy(r => r.Department).ToListAsync(ct))
                .Select(r => new SelectListItem(r.Department, r.DepartmentId.ToString())).ToList();
        if (model.Selected.HasSupplier)
            model.Suppliers = (await query.Where(r => r.SupplierId != null).Select(r => new { r.SupplierId, r.Supplier }).Distinct().OrderBy(r => r.Supplier).ToListAsync(ct))
                .Select(r => new SelectListItem(r.Supplier, r.SupplierId.ToString())).ToList();
        if (!string.IsNullOrEmpty(filter.Status)) query = query.Where(r => r.Status == filter.Status);
        if (filter.From.HasValue) { var from = filter.From.Value.Date; query = query.Where(r => r.OccurredAt >= from); }
        if (filter.To.HasValue) { var until = filter.To.Value.Date.AddDays(1); query = query.Where(r => r.OccurredAt < until); }
        if (model.Selected.HasDepartment && filter.DepartmentId.HasValue) query = query.Where(r => r.DepartmentId == filter.DepartmentId);
        if (model.Selected.HasSupplier && filter.SupplierId.HasValue) query = query.Where(r => r.SupplierId == filter.SupplierId);
        if (model.Selected.HasTender && !string.IsNullOrEmpty(filter.TenderNumber)) query = query.Where(r => r.TenderNumber.Contains(filter.TenderNumber));
        if (!string.IsNullOrEmpty(filter.Search)) query = query.Where(r => r.Reference.Contains(filter.Search) || r.Title.Contains(filter.Search) || r.Detail.Contains(filter.Search));
        model.TotalCount = await query.CountAsync(ct);
        if (export && model.TotalCount > ExportLimit) { model.Error = "More than 5,000 records match. Narrow your filters before exporting."; return model; }
        if (model.Selected.HasAmount)
            model.Totals = (await query.Where(r => r.Amount.HasValue).GroupBy(r => r.Currency)
                .Select(g => new { Currency = g.Key, Amount = g.Sum(r => r.Amount!.Value) }).ToListAsync(ct)).ToDictionary(r => r.Currency, r => r.Amount);
        filter.Page = Math.Min(filter.Page, model.PageCount);
        var sorted = filter.Sort switch
        {
            "oldest" => query.OrderBy(r => r.OccurredAt),
            "reference" => query.OrderBy(r => r.Reference),
            "status" => query.OrderBy(r => r.Status),
            "value" when model.Selected.HasAmount => query.OrderByDescending(r => r.Amount),
            _ => query.OrderByDescending(r => r.OccurredAt)
        };
        model.Rows = await sorted.ThenBy(r => r.Reference).ThenBy(r => r.RecordId)
            .Skip(export ? 0 : (filter.Page - 1) * model.PageSize).Take(export ? ExportLimit + 1 : model.PageSize).ToListAsync(ct);
        if (model.Rows.Count > ExportLimit) { model.Rows.Clear(); model.Error = "More than 5,000 records match. Narrow your filters before exporting."; return model; }
        if (filter.Type == "users") await FillUserRolesAsync(model.Rows, ct);
        foreach (var row in model.Rows)
        {
            if (filter.Type == "tenders") row.Detail = "Deadline: " + row.SecondaryDate?.ToString("dd MMM yyyy") + "; " + row.Count + " invitations / " + row.SecondCount + " submitted offers";
            if (filter.Type == "my-invitations") row.Detail = "Expires: " + row.SecondaryDate?.ToString("dd MMM yyyy") + "; " + (row.ThirdDate.HasValue ? "Responded: " + row.ThirdDate.Value.ToString("dd MMM yyyy") : "No response yet");
            if (filter.Type == "manager-decisions") row.Detail = row.SecondaryDate.HasValue && row.OccurredAt >= row.SecondaryDate
                ? Math.Floor((row.OccurredAt!.Value - row.SecondaryDate.Value).TotalDays) + " day(s) after submission" : "Duration not available";
        }
        await FillRecordDetailsAsync(filter.Type, userId, model.Rows, ct);
        return model;
    }

    private async Task FillUserRolesAsync(List<ReportRecordViewModel> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.RecordId).ToList();
        var roles = await _db.UserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId))
            .Join(_db.Roles.AsNoTracking(), r => r.RoleId, role => role.Id, (r, role) => new { r.UserId, role.Name }).ToListAsync(ct);
        foreach (var row in rows) row.Detail = string.Join(", ", roles.Where(r => r.UserId == row.RecordId).Select(r => r.Name)) + "; Last sign-in: " + (row.SecondaryDate?.ToString("dd MMM yyyy HH:mm") ?? "Never recorded");
    }
}
