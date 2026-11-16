using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Reports;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReportsController : Controller
{
    private readonly ReportQueryService _reports;
    private readonly ReportExcelWriter _excel;
    private readonly AuditService _audit;

    public ReportsController(ReportQueryService reports, ReportExcelWriter excel, AuditService audit)
    { _reports = reports; _excel = excel; _audit = audit; }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReportFilterViewModel filter, CancellationToken ct)
    {
        var page = await LoadAsync(filter, false, ct);
        return page == null ? Forbid() : View(page);
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] ReportFilterViewModel filter, string format = "xlsx", CancellationToken ct = default)
    {
        if (format != "xlsx") return BadRequest("Choose Excel export or Print / Save as PDF.");
        var page = await LoadAsync(filter, true, ct);
        if (page == null) return Forbid();
        if (page.Error != null || page.Selected == null) return BadRequest(page.Error ?? "Choose an available report.");
        var bytes = _excel.Write(page);
        await _audit.LogAsync("Exported", "Report", page.Selected.Key, $"format=xlsx; rows={page.Rows.Count}");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", page.Selected.Key + "-" + page.GeneratedAt.ToString("yyyyMMdd") + ".xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Print([FromQuery] ReportFilterViewModel filter, CancellationToken ct)
    {
        var page = await LoadAsync(filter, true, ct);
        if (page == null) return Forbid();
        if (page.Error != null || page.Selected == null) return BadRequest(page.Error ?? "Choose an available report.");
        page.IsPrint = true;
        // TODO RPT-01: direct PDF download after the lead approves a PDF package.
        await _audit.LogAsync("Exported", "Report", page.Selected.Key, $"format=print; rows={page.Rows.Count}");
        return View(page);
    }

    private async Task<ReportPageViewModel?> LoadAsync(ReportFilterViewModel filter, bool export, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(User.FindFirstValue(ClaimTypes.NameIdentifier))) return null;
        if (!string.IsNullOrEmpty(filter.Type) && !_reports.GetOptions(User).Any(o => o.Key == filter.Type)) return null;
        if (!ModelState.IsValid)
            return new ReportPageViewModel { Filter = filter, Options = _reports.GetOptions(User), Error = "Check the dates and filter values, then try again." };
        return await _reports.ReadAsync(User, filter, export, ct);
    }
}
