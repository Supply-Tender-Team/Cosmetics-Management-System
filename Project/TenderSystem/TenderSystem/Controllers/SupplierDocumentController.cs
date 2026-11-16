using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;

namespace TenderSystem.Controllers;

// OWNER: Wadea (AUTH-02). The legal documents a supplier uploads while registering.
// Only an Admin can open them. Every download is logged in DocumentAccessLogs.
// Link from the supplier review page:  <a asp-controller="SupplierDocument" asp-action="Download" asp-route-id="@doc.Id">...</a>
[Authorize(Roles = Roles.Admin)]
public class SupplierDocumentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _files;
    private readonly IWebHostEnvironment _environment;

    public SupplierDocumentController(ApplicationDbContext db, IFileStorageService files, IWebHostEnvironment environment)
    {
        _db = db;
        _files = files;
        _environment = environment;
    }

    // GET /SupplierDocument/Download/5
    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { d.FilePath, d.DocType })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        Stream? stream;
        try { stream = await _files.OpenReadAsync(document.FilePath, cancellationToken); }
        catch (InvalidOperationException) { return NotFound(); }
        if (stream is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var downloadedAt = DateTime.UtcNow;
        var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(
            _db, userId, "SupplierDocument", id, Statuses.DocumentAction.Download, downloadedAt, cancellationToken);

        var extension = Path.GetExtension(document.FilePath);
        var contentType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
        var downloadName = document.DocType.Replace(' ', '-') + extension;
        if (contentType == "application/pdf")
        {
            await using (stream)
            {
                var viewerName = history.FirstOrDefault()?.ViewerName ?? "Signed-in user";
                var watermarkedContent = await DocumentWatermarkService.CreateWatermarkedCopyAsync(
                    stream, downloadName, viewerName, downloadedAt, _environment.WebRootPath,
                    cancellationToken, history);
                Response.Headers.CacheControl = "no-store";
                return File(watermarkedContent, contentType, downloadName);
            }
        }

        return File(stream, contentType, downloadName);
    }
}
