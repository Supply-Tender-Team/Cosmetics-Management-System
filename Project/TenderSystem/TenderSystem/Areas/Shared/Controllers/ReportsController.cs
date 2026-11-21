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

    [HttpGet, Authorize(Roles = TenderSystem.Infrastructure.Roles.Admin)]
    public async Task<IActionResult> AiReportChoices(string type, CancellationToken ct)
    {
        // Start from this report's full authorized scope, not the regular table filters.
        var page = await LoadAsync(new ReportFilterViewModel { Type = type }, false, ct);
        if (page?.Selected == null) return Forbid();
        return Json(new { page.Suppliers, page.People, page.Departments, page.Tenders, page.Statuses });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = TenderSystem.Infrastructure.Roles.Admin)]
    public IActionResult GenerateAiReport(AiReportRequestViewModel input)
    {
        var option = _reports.GetOptions(User).FirstOrDefault(o => o.Key == input.Type);
        if (option == null) return Forbid();
        var validFocus = input.Focus switch
        {
            "supplier" => option.HasSupplier,
            "tender" => option.HasTender,
            "department" => option.HasDepartment,
            "person" => option.HasPerson,
            "period" => option.HasDate,
            "status" => true,
            "compare-suppliers" => option.HasSupplier,
            "compare-departments" => option.HasDepartment,
            _ => false
        };
        if (!ModelState.IsValid || (input.SpecificTopic && (!validFocus
            || (input.Focus is "supplier" or "tender" or "department" or "person" or "status" && string.IsNullOrWhiteSpace(input.FocusId))
            || (input.Focus == "period" && input.From == null && input.To == null)
            || (!option.HasDate && (input.From != null || input.To != null))
            || input.From > input.To || input.To?.Year >= 9999)))
            return BadRequest(new { message = "Choose a valid topic and date range, then try again." });

        // Preparation only: the dedicated report service has not been connected.
        // Never contact a provider, create a pretend result, or audit a successful generation here.
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            message = "AI report generation is not connected yet. Your selection is ready; regular reports remain available."
        });
    }

    [HttpGet]
    public async Task<IActionResult> FilterOptions([FromQuery] ReportFilterViewModel filter, CancellationToken ct)
    {
        var page = await LoadAsync(filter, false, ct);
        if (page == null) return Forbid();
        if (page.Error != null) return BadRequest(page.Error);
        return Json(new { page.Suppliers, page.People, page.Departments, page.Tenders, page.Statuses });
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
            await _audit.LogAsync("Exported", "Report", page.Selected.Key, $"format=pdf; rows={page.Rows.Count}");
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
