using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private static void NormalizeAdditionalFilters(ReportOptionViewModel option, ReportFilterViewModel filter)
    {
        filter.DepartmentIds = (filter.DepartmentIds ?? new()).Distinct().ToList();
        filter.SupplierIds = (filter.SupplierIds ?? new()).Distinct().ToList();
        filter.TenderNumbers = (filter.TenderNumbers ?? new()).Distinct().ToList();
        filter.Statuses = (filter.Statuses ?? new()).Distinct().ToList();
        if (!option.HasDepartment) filter.DepartmentIds.Clear();
        if (!option.HasSupplier) filter.SupplierIds.Clear();
        if (!option.HasTender) filter.TenderNumbers.Clear();
        if (option.Key.StartsWith("admin-", StringComparison.Ordinal))
        { filter.DepartmentIds.Clear(); filter.SupplierIds.Clear(); filter.TenderNumbers.Clear(); filter.Statuses.Clear(); }
        if (filter.DepartmentIds.Count > 0) filter.DepartmentId = null;
        if (filter.SupplierIds.Count > 0) filter.SupplierId = null;
        if (filter.TenderNumbers.Count > 0) filter.TenderNumber = null;
        if (filter.Statuses.Count > 0) filter.Status = null;
        if (!option.HasPerson || string.IsNullOrWhiteSpace(filter.PersonId)) filter.PersonId = null;
        if (!option.HasDepartment) filter.DepartmentId = null;
        if (!option.HasSupplier) filter.SupplierId = null;
        if (!option.HasTender || string.IsNullOrWhiteSpace(filter.TenderNumber)) filter.TenderNumber = null;
        if (option.Key.StartsWith("admin-", StringComparison.Ordinal))
        {
            // Admin uses named dropdowns instead of typing names or configuring sort/group rules.
            filter.Search = null;
            filter.Sort = option.HasDate ? "newest" : "reference";
        }
    }

    private async Task LoadAdditionalFilterChoicesAsync(ReportPageViewModel model, IQueryable<ReportRecordViewModel> query, CancellationToken ct)
    {
        var option = model.Selected!;
        model.Statuses = await ApplySimpleAdminFilters(query, model, "status").Select(r => r.Status).Distinct().OrderBy(s => s).ToListAsync(ct);
        if (option.HasPerson)
        {
            var people = await ApplySimpleAdminFilters(query, model, "person").Where(r => r.PersonId != null)
                .Select(r => new { r.PersonId, r.Person }).Distinct().OrderBy(r => r.Person).ToListAsync(ct);
            var ids = people.Select(p => p.PersonId).ToList();
            var emails = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
            model.People = people.Select(p => new SelectListItem(p.Person + " — " + emails.GetValueOrDefault(p.PersonId!), p.PersonId)).ToList();
        }
        if (option.HasDepartment)
            model.Departments = (await ApplySimpleAdminFilters(query, model, "department").Where(r => r.DepartmentId != null)
                .Select(r => new { r.DepartmentId, r.Department }).Distinct().OrderBy(r => r.Department).ToListAsync(ct))
                .Select(r => new SelectListItem(r.Department, r.DepartmentId.ToString())).ToList();
        if (option.HasTender)
        {
            var numbers = ApplySimpleAdminFilters(query, model, "tender").Select(r => r.TenderNumber).Distinct();
            model.Tenders = await _db.Tenders.AsNoTracking().Where(t => numbers.Contains(t.TenderNumber))
                .OrderByDescending(t => t.CreatedAt).Select(t => new SelectListItem(t.TenderNumber + " — " + t.Title, t.TenderNumber)).ToListAsync(ct);
        }
        if (option.HasSupplier)
        {
            var related = ApplySimpleAdminFilters(query, model, "supplier");
            if (option.Key == "admin-tenders")
            {
                var tenderIds = related.Select(r => r.TenderId);
                model.Suppliers = await _db.SupplierProfiles.AsNoTracking()
                    .Where(s => s.Invitations.Any(i => tenderIds.Contains(i.TenderId)) || s.Bids.Any(b => tenderIds.Contains(b.TenderId))
                        || s.TenderSuppliers.Any(t => tenderIds.Contains(t.TenderId)))
                    .OrderBy(s => s.CompanyName).Select(s => new SelectListItem(s.CompanyName, s.Id.ToString())).ToListAsync(ct);
            }
            else
                model.Suppliers = (await related.Where(r => r.SupplierId != null).Select(r => new { r.SupplierId, r.Supplier })
                    .Distinct().OrderBy(r => r.Supplier).ToListAsync(ct)).Select(r => new SelectListItem(r.Supplier, r.SupplierId.ToString())).ToList();
        }
    }

    private IQueryable<ReportRecordViewModel> ApplySimpleAdminFilters(IQueryable<ReportRecordViewModel> query, ReportPageViewModel model, string? skip = null)
    {
        var filter = model.Filter;
        var option = model.Selected!;
        if (skip != "person" && option.HasPerson && filter.PersonId != null) query = query.Where(r => r.PersonId == filter.PersonId);
        if (skip != "department" && option.HasDepartment && filter.DepartmentId.HasValue) query = query.Where(r => r.DepartmentId == filter.DepartmentId);
        if (skip != "tender" && option.HasTender && filter.TenderNumber != null) query = query.Where(r => r.TenderNumber == filter.TenderNumber);
        if (skip != "status" && !string.IsNullOrWhiteSpace(filter.Status)) query = query.Where(r => r.Status == filter.Status);
        // Supplier activity dates are applied to invitations, submitted offers and final awards in its query.
        if (option.Key != "admin-supplier-activity")
        {
            if (filter.From.HasValue) { var from = filter.From.Value.Date; query = query.Where(r => r.OccurredAt >= from); }
            if (filter.To.HasValue) { var until = filter.To.Value.Date.AddDays(1); query = query.Where(r => r.OccurredAt < until); }
        }
        if (skip != "supplier" && option.HasSupplier && filter.SupplierId.HasValue)
        {
            var supplierId = filter.SupplierId.Value;
            query = option.Key == "admin-tenders"
                ? query.Where(r => _db.Tenders.Any(t => t.Id == r.TenderId &&
                    (t.Invitations.Any(i => i.SupplierProfileId == supplierId) || t.Bids.Any(b => b.SupplierProfileId == supplierId)
                        || t.TenderSuppliers.Any(s => s.SupplierProfileId == supplierId))))
                : query.Where(r => r.SupplierId == supplierId);
        }
        return query;
    }

    private static void SetAppliedFilters(ReportPageViewModel model)
    {
        var f = model.Filter;
        void Add(string label, string? value)
        { if (!string.IsNullOrWhiteSpace(value)) model.AppliedFilters.Add(new() { Label = label, Value = value }); }
        Add("Search", f.Search); Add(model.Selected!.StatusLabel, f.Status);
        Add("From", f.From?.ToString("yyyy-MM-dd")); Add("To", f.To?.ToString("yyyy-MM-dd"));
        if (f.DepartmentId.HasValue) Add("Department", model.Departments.FirstOrDefault(d => d.Value == f.DepartmentId.ToString())?.Text ?? "No matching department");
        if (f.SupplierId.HasValue) Add(model.Selected.SupplierLabel, model.Suppliers.FirstOrDefault(s => s.Value == f.SupplierId.ToString())?.Text ?? "No matching supplier");
        if (f.PersonId != null) Add(model.Selected.PersonLabel, model.People.FirstOrDefault(p => p.Value == f.PersonId)?.Text ?? "No matching person");
        if (f.TenderNumber != null) Add("Tender", model.Tenders.FirstOrDefault(t => t.Value == f.TenderNumber)?.Text ?? f.TenderNumber);
        if (f.DepartmentIds.Count > 0) Add("Departments", string.Join(" or ", f.DepartmentIds.Select(id => model.Departments.FirstOrDefault(d => d.Value == id.ToString())?.Text ?? "No matching department")));
        if (f.SupplierIds.Count > 0) Add("Suppliers", string.Join(" or ", f.SupplierIds.Select(id => model.Suppliers.FirstOrDefault(s => s.Value == id.ToString())?.Text ?? "No matching supplier")));
        if (f.TenderNumbers.Count > 0) Add("Tenders", string.Join(" or ", f.TenderNumbers));
        if (f.Statuses.Count > 0) Add("Statuses", string.Join(" or ", f.Statuses));
    }
}
