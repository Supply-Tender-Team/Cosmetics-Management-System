using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private IQueryable<ReportRecordViewModel> ApplyTreeFilters(IQueryable<ReportRecordViewModel> query, ReportPageViewModel page, string? skip = null)
    {
        var f = page.Filter;
        var option = page.Selected!;
        if (skip != "status")
        {
            if (f.Statuses.Count > 0) query = query.Where(r => f.Statuses.Contains(r.Status));
            else if (!string.IsNullOrEmpty(f.Status)) query = query.Where(r => r.Status == f.Status);
        }
        if (option.HasDepartment && skip != "department")
        {
            if (f.DepartmentIds.Count > 0) query = query.Where(r => r.DepartmentId != null && f.DepartmentIds.Contains(r.DepartmentId.Value));
            else if (f.DepartmentId.HasValue) query = query.Where(r => r.DepartmentId == f.DepartmentId);
        }
        if (option.HasSupplier && skip != "supplier")
        {
            if (f.SupplierIds.Count > 0) query = query.Where(r => r.SupplierId != null && f.SupplierIds.Contains(r.SupplierId.Value));
            else if (f.SupplierId.HasValue) query = query.Where(r => r.SupplierId == f.SupplierId);
        }
        if (option.HasTender && skip != "tender")
        {
            if (f.TenderNumbers.Count > 0) query = query.Where(r => f.TenderNumbers.Contains(r.TenderNumber));
            else if (!string.IsNullOrEmpty(f.TenderNumber)) query = query.Where(r => r.TenderNumber.Contains(f.TenderNumber));
        }
        if (f.From.HasValue) { var from = f.From.Value.Date; query = query.Where(r => r.OccurredAt >= from); }
        if (f.To.HasValue) { var until = f.To.Value.Date.AddDays(1); query = query.Where(r => r.OccurredAt < until); }
        if (!string.IsNullOrEmpty(f.Search)) query = query.Where(r => r.Reference.Contains(f.Search) || r.Title.Contains(f.Search) || r.Detail.Contains(f.Search));
        return query;
    }

    private async Task LoadTreeChoicesAsync(ReportPageViewModel page, IQueryable<ReportRecordViewModel> scoped, CancellationToken ct)
    {
        // Every facet starts from CreateQuery's role/owner scope. Never use a global directory.
        page.Statuses = await ApplyTreeFilters(scoped, page, "status").Select(r => r.Status).Distinct().OrderBy(s => s).ToListAsync(ct);
        if (page.Selected!.HasDepartment)
            page.Departments = (await ApplyTreeFilters(scoped, page, "department").Where(r => r.DepartmentId != null)
                .Select(r => new { r.DepartmentId, r.Department }).Distinct().OrderBy(r => r.Department).ToListAsync(ct))
                .Select(r => new SelectListItem(r.Department, r.DepartmentId.ToString())).ToList();
        if (page.Selected.HasSupplier)
            page.Suppliers = (await ApplyTreeFilters(scoped, page, "supplier").Where(r => r.SupplierId != null)
                .Select(r => new { r.SupplierId, r.Supplier }).Distinct().OrderBy(r => r.Supplier).ToListAsync(ct))
                .Select(r => new SelectListItem(r.Supplier, r.SupplierId.ToString())).ToList();
        if (page.Selected.HasTender)
        {
            var numbers = ApplyTreeFilters(scoped, page, "tender").Where(r => r.TenderNumber != "").Select(r => r.TenderNumber).Distinct();
            page.Tenders = await _db.Tenders.AsNoTracking().Where(t => numbers.Contains(t.TenderNumber))
                .OrderByDescending(t => t.CreatedAt).Select(t => new SelectListItem(t.TenderNumber + " — " + t.Title, t.TenderNumber)).ToListAsync(ct);
        }
    }
}
