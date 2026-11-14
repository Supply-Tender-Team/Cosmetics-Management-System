using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Files;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class ProfileController : Controller
{
    // OWNER: Sara

    private static readonly string[] AllowedDocumentTypes =
    [
        "Commercial registration",
        "Tax certificate",
        "Other document"
    ];

    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly AuditService _audit;

    public ProfileController(ApplicationDbContext db, IFileStorageService fileStorage, AuditService audit)
    {
        _db = db;
        _fileStorage = fileStorage;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(tracked: false, cancellationToken);
        if (profile is null) return NotFound();

        return View(await BuildPageModelAsync(profile, cancellationToken));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Profile")] SupplierProfileEditViewModel model,
        CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(tracked: true, cancellationToken);
        if (profile is null) return NotFound();

        if (!ModelState.IsValid)
            return await ReturnProfilePageAsync(profile, model, null, cancellationToken);

        profile.CompanyName = model.CompanyName.Trim();
        profile.TaxNumber = model.TaxNumber.Trim();
        profile.CommercialRegNumber = model.CommercialRegNumber.Trim();
        profile.Country = model.Country.Trim();
        profile.City = model.City.Trim();
        profile.Address = model.Address.Trim();
        profile.ContactPerson = model.ContactPerson.Trim();
        profile.ContactPhone = model.ContactPhone.Trim();
        profile.Website = string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim();
        profile.Category = model.Category.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Updated", "SupplierProfile", profile.Id.ToString(), "Company profile updated.");
        TempData["ProfileSuccess"] = "Your company profile was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadDocument([Bind(Prefix = "DocumentUpload")] SupplierDocumentUploadViewModel model,
        CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(tracked: false, cancellationToken);
        if (profile is null) return NotFound();

        if (!AllowedDocumentTypes.Contains(model.DocType, StringComparer.Ordinal))
            ModelState.AddModelError("DocumentUpload.DocType", "Choose a document type from the list.");
        if (model.File is null || model.File.Length == 0)
            ModelState.AddModelError("DocumentUpload.File", "Choose a file to upload.");

        if (!ModelState.IsValid || model.File is null)
            return await ReturnProfilePageAsync(profile, null, model, cancellationToken);

        try
        {
            await using var stream = model.File.OpenReadStream();
            var stored = await _fileStorage.SaveAsync(stream, model.File.FileName, model.File.ContentType, cancellationToken);
            var document = new SupplierDocument
            {
                SupplierProfileId = profile.Id,
                DocType = model.DocType,
                FilePath = stored.RelativePath,
                ExpiryDate = model.ExpiryDate?.Date,
                UploadedAt = DateTime.UtcNow
            };
            _db.SupplierDocuments.Add(document);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Uploaded", "SupplierDocument", document.Id.ToString(), "Company document uploaded.");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("DocumentUpload.File", ex.Message);
            return await ReturnProfilePageAsync(profile, null, model, cancellationToken);
        }
        catch (IOException)
        {
            ModelState.AddModelError(string.Empty, "The file could not be saved. Please try again.");
            return await ReturnProfilePageAsync(profile, null, model, cancellationToken);
        }
        catch (System.Net.Http.HttpRequestException)
        {
            ModelState.AddModelError(string.Empty, "The file storage service is unavailable. Please try again later.");
            return await ReturnProfilePageAsync(profile, null, model, cancellationToken);
        }

        TempData["ProfileSuccess"] = "The document was uploaded.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(int id, CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(tracked: false, cancellationToken);
        if (profile is null) return NotFound();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(item => item.Id == id && item.SupplierProfileId == profile.Id)
            .Select(item => new { item.DocType, item.FilePath, item.UploadedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        try
        {
            var stream = await _fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
            if (stream is null) return NotFound();

            var extension = Path.GetExtension(document.FilePath);
            var fileName = $"{document.DocType.Replace(' ', '-')}-{document.UploadedAt:yyyyMMdd}{extension}";
            var contentTypeProvider = new FileExtensionContentTypeProvider();
            if (!contentTypeProvider.TryGetContentType(fileName, out var contentType))
                contentType = "application/octet-stream";

            return File(stream, contentType, fileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private async Task<SupplierProfile?> GetCurrentProfileAsync(bool tracked, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return null;

        IQueryable<SupplierProfile> query = _db.SupplierProfiles;
        if (!tracked) query = query.AsNoTracking();

        return await query.SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
    }

    private async Task<SupplierProfilePageViewModel> BuildPageModelAsync(SupplierProfile profile,
        CancellationToken cancellationToken)
    {
        var documents = await _db.SupplierDocuments.AsNoTracking()
            .Where(document => document.SupplierProfileId == profile.Id)
            .OrderByDescending(document => document.UploadedAt)
            .Select(document => new SupplierDocumentSummaryViewModel
            {
                Id = document.Id,
                DocType = document.DocType,
                ExpiryDate = document.ExpiryDate,
                UploadedAt = document.UploadedAt,
                FileExtension = System.IO.Path.GetExtension(document.FilePath)
            })
            .ToListAsync(cancellationToken);

        return new SupplierProfilePageViewModel
        {
            Profile = new SupplierProfileEditViewModel
            {
                CompanyName = profile.CompanyName,
                TaxNumber = profile.TaxNumber,
                CommercialRegNumber = profile.CommercialRegNumber,
                Country = profile.Country,
                City = profile.City,
                Address = profile.Address,
                ContactPerson = profile.ContactPerson,
                ContactPhone = profile.ContactPhone,
                Website = profile.Website,
                Category = profile.Category
            },
            SupplierStatus = profile.Status,
            Documents = documents
        };
    }

    private async Task<IActionResult> ReturnProfilePageAsync(SupplierProfile profile,
        SupplierProfileEditViewModel? editModel, SupplierDocumentUploadViewModel? uploadModel,
        CancellationToken cancellationToken)
    {
        var pageModel = await BuildPageModelAsync(profile, cancellationToken);
        if (editModel is not null) pageModel.Profile = editModel;
        if (uploadModel is not null) pageModel.DocumentUpload = uploadModel;
        return View("Index", pageModel);
    }
}
