using Microsoft.AspNetCore.Mvc.Rendering;

namespace TenderSystem.Areas.Shared.Models.Reports;

public class ReportPageViewModel
{
    public ReportFilterViewModel Filter { get; set; } = new();
    public List<ReportOptionViewModel> Options { get; set; } = new();
    public ReportOptionViewModel? Selected { get; set; }
    public List<ReportRecordViewModel> Rows { get; set; } = new();
    public List<string> Statuses { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Suppliers { get; set; } = new();
    public List<SelectListItem> People { get; set; } = new();
    public List<SelectListItem> Tenders { get; set; } = new();
    public List<ReportFieldViewModel> AppliedFilters { get; set; } = new();
    public List<ReportFieldViewModel> Metrics { get; set; } = new();
    public List<ReportChartViewModel> Charts { get; set; } = new();
    public List<string> CoverageNotes { get; set; } = new();
    public List<string> TenderStatuses { get; set; } = new();
    public List<ReportFieldViewModel> Breakdown { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 25;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public Dictionary<string, decimal> Totals { get; set; } = new();
    public bool IsPrint { get; set; }
    public string? Error { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> RouteValues(int? page = null)
    {
        var values = new Dictionary<string, string> { ["type"] = Selected?.Key ?? "", ["search"] = Filter.Search ?? "",
            ["status"] = Filter.Status ?? "", ["tenderStatus"] = Filter.TenderStatus ?? "", ["tenderNumber"] = Filter.TenderNumber ?? "",
            ["from"] = Filter.From?.ToString("yyyy-MM-dd") ?? "", ["to"] = Filter.To?.ToString("yyyy-MM-dd") ?? "",
            ["departmentId"] = Filter.DepartmentId?.ToString() ?? "", ["supplierId"] = Filter.SupplierId?.ToString() ?? "",
            ["personId"] = Filter.PersonId ?? "",
            ["sort"] = Filter.Sort, ["page"] = (page ?? Filter.Page).ToString() };
        // Indexed query keys preserve every selection in paging, print and both exports.
        for (var i = 0; i < Filter.DepartmentIds.Count; i++) values[$"departmentIds[{i}]"] = Filter.DepartmentIds[i].ToString();
        for (var i = 0; i < Filter.SupplierIds.Count; i++) values[$"supplierIds[{i}]"] = Filter.SupplierIds[i].ToString();
        for (var i = 0; i < Filter.TenderNumbers.Count; i++) values[$"tenderNumbers[{i}]"] = Filter.TenderNumbers[i];
        for (var i = 0; i < Filter.Statuses.Count; i++) values[$"statuses[{i}]"] = Filter.Statuses[i];
        return values;
    }
}
