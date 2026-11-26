using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Ai;
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
    private readonly AiReportService _aiReport;
    private readonly AiReportPdfWriter _aiPdf;
    private readonly AiReportSnapshotStore _aiSnapshots;

    public ReportsController(ReportQueryService reports, ReportExcelWriter excel, AuditService audit,
        ReportPdfWriter pdf, IWebHostEnvironment environment, AiReportService aiReport, AiReportPdfWriter aiPdf, AiReportSnapshotStore aiSnapshots)
    { _reports = reports; _excel = excel; _audit = audit; _pdf = pdf; _environment = environment; _aiReport = aiReport; _aiPdf = aiPdf; _aiSnapshots = aiSnapshots; }

    [HttpGet, Authorize(Roles = TenderSystem.Infrastructure.Roles.Admin)]
    public async Task<IActionResult> ContractDocument(int id, [FromServices] TenderSystem.Data.ApplicationDbContext db,
        [FromServices] TenderSystem.Services.Files.IFileStorageService files, CancellationToken ct)
    {
        var contract = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Contracts).Where(c => c.Id == id), ct);
        if (contract == null || string.IsNullOrWhiteSpace(contract.DocumentPath)) return NotFound();
        var stream = await files.OpenReadAsync(contract.DocumentPath, ct);
        if (stream == null) return NotFound();
        try {
            ct.ThrowIfCancellationRequested();
            await _audit.LogAsync("ContractDocumentDownloaded", "Contract", id.ToString(), "Downloaded through authorized Admin Reports.");
        } catch { await stream.DisposeAsync(); throw; }
        var extension = Path.GetExtension(contract.DocumentPath).ToLowerInvariant();
        if (extension is not ".pdf" and not ".docx" and not ".png" and not ".jpg" and not ".jpeg") extension = ".bin";
        return File(stream, "application/octet-stream", "Contract-" + id + extension);
    }

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
        return Json(new { page.Suppliers, page.People, page.Departments, page.Tenders, page.Statuses, page.TenderStatuses, page.DeliveryModes });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = TenderSystem.Infrastructure.Roles.Admin)]
    public async Task<IActionResult> GenerateAiReport(AiReportRequestViewModel input, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!User.IsInRole(TenderSystem.Infrastructure.Roles.Admin) || string.IsNullOrEmpty(userId)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(new { ok = false, status = "invalid-request", message = "Check the report topic and dates, then try again." });
        AiReportFacts? facts;
        try { facts = await _reports.ReadAiFactsAsync(User, input, ct); }
        catch (ArgumentException) { return BadRequest(new { ok = false, status = "invalid-request", message = "Choose an available report topic and a valid date range." }); }
        if (facts == null) return Forbid();
        // One analysis call. Expand and PDF export never request another narrative.
        var result = await _aiReport.AnalyzeAsync(facts, ct);
        ct.ThrowIfCancellationRequested();
        if (!result.Ok || result.Narrative == null)
        {
            var message = result.Status switch
            {
                "no-data" => "No records match this report scope. Choose another topic or date range.",
                "disabled" => "AI analysis is not configured. Verified figures are shown below; regular reports still work.",
                "timeout" => "The analysis took too long. Please try again.",
                "rejected" => "The analysis did not pass the checks and has not been shown. Please try again.",
                "invalid-request" or "too-large" => "The analysis request could not be prepared. Please try another scope.",
                _ => string.IsNullOrWhiteSpace(result.Message) ? "AI analysis is temporarily unavailable. Please try again." : result.Message
            };
            return Json(new { ok = false, status = result.Status, message, title = facts.Title, scope = facts.Scope,
                recordCount = facts.RecordCount, figures = facts.KeyFigures, coverageNotes = facts.Limits });
        }
        var narrative = result.Narrative;
        var report = new AiReportViewModel
        {
            Title = facts.Title, Scope = facts.Scope, RecordCount = facts.RecordCount,
            GeneratedBy = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Signed-in Admin",
            GeneratedAt = DateTime.UtcNow,
            KeyFigures = facts.KeyFigures.Select(f => new ReportFieldViewModel { Label = f.Label, Value = f.Value }).ToList(),
            Summary = narrative.Summary, Findings = narrative.Findings.ToList(), NeedsAttention = narrative.NeedsAttention.ToList(),
            Details = narrative.Details.Select(f => new ReportFieldViewModel { Label = f.Label, Value = f.Value }).ToList(),
            CoverageNotes = narrative.CoverageNotes.ToList()
        };
        await _audit.LogAsync("AiReportGenerated", "Report", input.Type,
            $"scopeMode={(input.SpecificTopic ? input.Focus : "overview")}; focusId={(input.SpecificTopic ? input.FocusId : null)}; from={(input.SpecificTopic ? input.From : null):yyyy-MM-dd}; to={(input.SpecificTopic ? input.To : null):yyyy-MM-dd}; records={facts.RecordCount}; model={result.Model}");
        ct.ThrowIfCancellationRequested();
        var token = await _aiSnapshots.SaveAsync(userId, input.Type, report, ct);
        Response.Headers["X-AI-Report-Export"] = Url.Action(nameof(ExportGeneratedAiReport), new { id = token });
        return PartialView("~/Areas/Shared/Views/Reports/_AiGeneratedReport.cshtml", report);
    }

    [HttpGet, Authorize(Roles = TenderSystem.Infrastructure.Roles.Admin)]
    public async Task<IActionResult> ExportGeneratedAiReport(string id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!User.IsInRole(TenderSystem.Infrastructure.Roles.Admin) || string.IsNullOrEmpty(userId)) return Forbid();
        var report = await _aiSnapshots.ReadAsync(userId, id ?? "", ct);
        if (report == null) return NotFound("This generated report was replaced by a newer one. Generate the report again before exporting.");
        var bytes = await _aiPdf.WriteAsync(report, ct);
        ct.ThrowIfCancellationRequested();
        await _audit.LogAsync("AiReportExported", "Report", "GeneratedSnapshot", $"format=pdf; records={report.RecordCount}; generatedAt={report.GeneratedAt:O}");
        return File(bytes, "application/pdf", "MS-WAND-AI-Report-" + report.GeneratedAt.ToString("yyyyMMdd-HHmm") + ".pdf");
    }

    // The last AI report of this report type (saved in the database): the page shows it again after a refresh instead of calling the model again.
    [HttpGet, Authorize(Roles = TenderSystem.Infrastructure.Roles.Admin)]
    public async Task<IActionResult> LastAiReport(string? type, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!User.IsInRole(TenderSystem.Infrastructure.Roles.Admin) || string.IsNullOrEmpty(userId)) return Forbid();
        var last = await _aiSnapshots.LastAsync(userId, type ?? "", ct);
        if (last is null) return NoContent();
        Response.Headers["X-AI-Report-Export"] = Url.Action(nameof(ExportGeneratedAiReport), new { id = last.Value.Token });
        return PartialView("~/Areas/Shared/Views/Reports/_AiGeneratedReport.cshtml", last.Value.Report);
    }

    [HttpGet]
    public async Task<IActionResult> FilterOptions([FromQuery] ReportFilterViewModel filter, CancellationToken ct)
    {
        var page = await LoadAsync(filter, false, ct);
        if (page == null) return Forbid();
        if (page.Error != null) return BadRequest(page.Error);
        return Json(new { page.Suppliers, page.People, page.Departments, page.Tenders, page.Statuses, page.TenderStatuses, page.DeliveryModes });
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
