using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Models;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Files;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class ProfileController : Controller
{
    // OWNER: Sara

    private static readonly string[] DocumentTypes = ["Commercial registration", "Tax certificate", "Other document"];
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _files;
    private readonly AuditService _audit;

    public ProfileController(ApplicationDbContext db, IFileStorageService files, AuditService audit)
    {
        _db = db;
        _files = files;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        var supplierProfile = await _db.SupplierProfiles.AsNoTracking()
            .Include(profile => profile.User)
            .Include(profile => profile.SupplierDocuments)
            .SingleOrDefaultAsync(profile => profile.UserId == supplierUserId, cancellationToken);
        if (supplierProfile is null) return NotFound();

        ViewBag.Categories = RegisterSupplierViewModel.Categories;
        return View(BuildViewModel(supplierProfile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SupplierProfileViewModel model, CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        var supplierProfile = await _db.SupplierProfiles
            .Include(profile => profile.User)
            .Include(profile => profile.SupplierDocuments)
            .SingleOrDefaultAsync(profile => profile.UserId == supplierUserId, cancellationToken);
        if (supplierProfile is null) return NotFound();

        if (!RegisterSupplierViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Choose a company category.");

        var taxNumber = model.TaxNumber.Trim();
        if (!string.Equals(taxNumber, supplierProfile.TaxNumber, StringComparison.OrdinalIgnoreCase)
            && await _db.SupplierProfiles.AnyAsync(profile => profile.Id != supplierProfile.Id
                && profile.TaxNumber == taxNumber, cancellationToken))
            ModelState.AddModelError(nameof(model.TaxNumber), "Another company already uses this tax number.");

        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            var website = model.Website.Trim();
            if (!website.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !website.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                website = "https://" + website;

            if (Uri.TryCreate(website, UriKind.Absolute, out var uri) && uri.Host.Contains('.'))
                model.Website = uri.ToString().TrimEnd('/');
            else
                ModelState.AddModelError(nameof(model.Website), "Enter a valid website address.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.IsEditing = true;
            model.Email = supplierProfile.User.Email ?? string.Empty;
            model.Documents = supplierProfile.SupplierDocuments.OrderByDescending(document => document.UploadedAt)
                .Select(ToDocumentViewModel).ToList();
            ViewBag.Categories = RegisterSupplierViewModel.Categories;
            return View("Index", model);
        }

        supplierProfile.CompanyName = model.CompanyName.Trim();
        supplierProfile.TaxNumber = taxNumber;
        supplierProfile.CommercialRegNumber = model.CommercialRegNumber.Trim();
        supplierProfile.Category = model.Category;
        supplierProfile.Country = model.Country.Trim();
        supplierProfile.City = model.City.Trim();
        supplierProfile.Address = model.Address.Trim();
        supplierProfile.Website = string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim();
        supplierProfile.ContactPerson = model.ContactPerson.Trim();
        supplierProfile.ContactPhone = model.ContactPhone.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Updated", "SupplierProfile", supplierProfile.Id.ToString(), "Company profile updated.");
        TempData["Success"] = "Company profile updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Upload(IFormFile? file, string documentType, DateTime? expiryDate,
        CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        if (!DocumentTypes.Contains(documentType))
        {
            TempData["Error"] = "Choose a valid document type.";
            return RedirectToAction(nameof(Index));
        }
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Index));
        }

        var supplierProfile = await _db.SupplierProfiles
            .SingleOrDefaultAsync(profile => profile.UserId == supplierUserId, cancellationToken);
        if (supplierProfile is null) return NotFound();

        try
        {
            await using var stream = file.OpenReadStream();
            var stored = await _files.SaveAsync(stream, file.FileName, file.ContentType, cancellationToken);
            var uploadedDocument = new SupplierDocument
            {
                SupplierProfileId = supplierProfile.Id,
                DocType = documentType,
                FilePath = stored.RelativePath,
                ExpiryDate = expiryDate?.Date,
                UploadedAt = DateTime.UtcNow
            };
            _db.SupplierDocuments.Add(uploadedDocument);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Uploaded", "SupplierDocument", uploadedDocument.Id.ToString(), "Company document uploaded.");
            TempData["Success"] = "Company document uploaded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(document => document.Id == id && document.SupplierProfile.UserId == supplierUserId)
            .Select(document => new { document.DocType, document.FilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        try
        {
            await using var stream = await _files.OpenReadAsync(document.FilePath, cancellationToken);
            if (stream is null) return NotFound();

            var extension = Path.GetExtension(document.FilePath);
            var contentType = extension.ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
            var viewer = await _db.Users.AsNoTracking()
                .Where(user => user.Id == supplierUserId)
                .Select(user => new { user.FullName, user.Email })
                .SingleOrDefaultAsync(cancellationToken);
            if (viewer is null) return Challenge();

            var downloadedAt = DateTime.UtcNow;
            var viewerName = string.IsNullOrWhiteSpace(viewer.FullName) ? viewer.Email ?? "Signed-in user" : viewer.FullName;
            var webRoot = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
            var downloadName = document.DocType.Replace(' ', '-') + extension;
            var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(
                _db, supplierUserId, "SupplierDocument", id, Statuses.DocumentAction.Download, downloadedAt, cancellationToken);
            var watermarkedContent = await DocumentWatermarkService.CreateWatermarkedCopyAsync(
                stream, downloadName, viewerName, downloadedAt, webRoot, cancellationToken, history);
            if (contentType == "application/pdf") Response.Headers.CacheControl = "no-store";

            return File(watermarkedContent, contentType, downloadName);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private static SupplierProfileViewModel BuildViewModel(SupplierProfile profile) => new()
    {
        CompanyName = profile.CompanyName,
        TaxNumber = profile.TaxNumber,
        CommercialRegNumber = profile.CommercialRegNumber,
        Category = profile.Category,
        Country = profile.Country,
        City = profile.City,
        Address = profile.Address,
        Website = profile.Website,
        ContactPerson = profile.ContactPerson,
        ContactPhone = profile.ContactPhone,
        Email = profile.User.Email ?? string.Empty,
        Documents = profile.SupplierDocuments.OrderByDescending(document => document.UploadedAt)
            .Select(ToDocumentViewModel).ToList()
    };

    private static SupplierProfileDocumentViewModel ToDocumentViewModel(SupplierDocument document) => new()
    {
        Id = document.Id,
        DocumentType = document.DocType,
        FileExtension = Path.GetExtension(document.FilePath),
        ExpiryDate = document.ExpiryDate,
        UploadedAt = document.UploadedAt
    };
}
