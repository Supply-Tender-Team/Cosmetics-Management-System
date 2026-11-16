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
    private readonly ReportPdfWriter _pdf;
    private readonly IWebHostEnvironment _environment;

    public ReportsController(ReportQueryService reports, ReportExcelWriter excel, AuditService audit,
        ReportPdfWriter pdf, IWebHostEnvironment environment)
    { _reports = reports; _excel = excel; _audit = audit; _pdf = pdf; _environment = environment; }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReportFilterViewModel filter, CancellationToken ct)
    {
        var page = await LoadAsync(filter, false, ct);
        return page == null ? Forbid() : View(page);
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] ReportFilterViewModel filter, string format = "xlsx", CancellationToken ct = default)
    {
        if (format != "xlsx" && format != "pdf") return BadRequest("Choose Excel or PDF export.");
        var page = await LoadAsync(filter, true, ct);
        if (page == null) return Forbid();
        if (page.Error != null || page.Selected == null) return BadRequest(page.Error ?? "Choose an available report.");
        if (format == "pdf")
        {
            var pdf = await _pdf.WriteAsync(page, _environment.WebRootPath, ct);
            await _audit.LogAsync("Exported", "Report", page.Selected.Key, $"format=pdf; matchingRows={page.Rows.Count}; pageLimit=4");
            return File(pdf, "application/pdf", page.Selected.Key + "-" + page.GeneratedAt.ToString("yyyyMMdd") + ".pdf");
        }
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
