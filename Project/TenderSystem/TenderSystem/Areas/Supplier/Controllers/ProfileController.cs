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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var profile = await _db.SupplierProfiles.AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.SupplierDocuments)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return NotFound();

        ViewBag.Categories = RegisterSupplierViewModel.Categories;
        return View(BuildViewModel(profile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SupplierProfileViewModel model, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var profile = await _db.SupplierProfiles.Include(x => x.User).Include(x => x.SupplierDocuments)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return NotFound();

        if (!RegisterSupplierViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Choose a company category.");

        var taxNumber = model.TaxNumber.Trim();
        if (!string.Equals(taxNumber, profile.TaxNumber, StringComparison.OrdinalIgnoreCase)
            && await _db.SupplierProfiles.AnyAsync(x => x.Id != profile.Id && x.TaxNumber == taxNumber, cancellationToken))
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
            model.Email = profile.User.Email ?? string.Empty;
            model.Documents = profile.SupplierDocuments.OrderByDescending(x => x.UploadedAt)
                .Select(ToDocumentViewModel).ToList();
            ViewBag.Categories = RegisterSupplierViewModel.Categories;
            return View("Index", model);
        }

        profile.CompanyName = model.CompanyName.Trim();
        profile.TaxNumber = taxNumber;
        profile.CommercialRegNumber = model.CommercialRegNumber.Trim();
        profile.Category = model.Category;
        profile.Country = model.Country.Trim();
        profile.City = model.City.Trim();
        profile.Address = model.Address.Trim();
        profile.Website = string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim();
        profile.ContactPerson = model.ContactPerson.Trim();
        profile.ContactPhone = model.ContactPhone.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Updated", "SupplierProfile", profile.Id.ToString(), "Company profile updated.");
        TempData["Success"] = "Company profile updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Upload(IFormFile? file, string documentType, DateTime? expiryDate,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

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

        var profile = await _db.SupplierProfiles.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return NotFound();

        try
        {
            await using var stream = file.OpenReadStream();
            var stored = await _files.SaveAsync(stream, file.FileName, file.ContentType, cancellationToken);
            var uploadedDocument = new SupplierDocument
            {
                SupplierProfileId = profile.Id,
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(x => x.Id == id && x.SupplierProfile.UserId == userId)
            .Select(x => new { x.DocType, x.FilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        try
        {
            var stream = await _files.OpenReadAsync(document.FilePath, cancellationToken);
            if (stream is null) return NotFound();

            _db.DocumentAccessLogs.Add(new DocumentAccessLog
            {
                UserId = userId,
                DocumentKind = "SupplierDocument",
                DocumentId = id,
                Action = Statuses.DocumentAction.Download,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);

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
            return File(stream, contentType, document.DocType.Replace(' ', '-') + extension);
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
        Documents = profile.SupplierDocuments.OrderByDescending(x => x.UploadedAt)
            .Select(ToDocumentViewModel).ToList()
    };

    private static SupplierProfileDocumentViewModel ToDocumentViewModel(SupplierDocument document) => new()
    {
        Id = document.Id,
        DocumentType = document.DocType,
        ExpiryDate = document.ExpiryDate,
        UploadedAt = document.UploadedAt
    };
}
