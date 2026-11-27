using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Models;
using TenderSystem.Services.Files;

namespace TenderSystem.Controllers;

// OWNER: Wadea. Supplier self-registration: the form, the checks, the account and its documents.
// The same AccountController (a partial class): the fields and the constructor are in AccountController.cs.
public partial class AccountController
{
    // ───────────────────────── Supplier registration ─────────────────────────

    [HttpGet]
    public IActionResult RegisterSupplier()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect(DashboardRouter.UrlFor(User));
        return View(new RegisterSupplierViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterSupplier(RegisterSupplierViewModel model)
    {
        if (!model.AcceptTerms)
            ModelState.AddModelError(nameof(model.AcceptTerms), "You must accept the terms to register.");

        // Website is optional. Accept "www.example.com" and add https:// ourselves.
        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            var site = model.Website.Trim();
            if (!site.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                site = "https://" + site;

            if (Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Host.Contains('.'))
                model.Website = uri.ToString().TrimEnd('/');
            else
                ModelState.AddModelError(nameof(model.Website), "Enter a valid website, for example www.example.com");
        }

        if (!RegisterSupplierViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Please choose a category.");
        if (await _db.SupplierProfiles.AnyAsync(p => p.TaxNumber == model.TaxNumber))
            ModelState.AddModelError(nameof(model.TaxNumber), "A company with this tax number is already registered.");
        if (await _users.FindByEmailAsync(model.Email) != null)
            ModelState.AddModelError(nameof(model.Email), "This email is already registered.");
        var uploads = new List<(string Type, IFormFile File, DateTime? Expiry)>();
        CheckDocument(model.CommercialRegistrationFile, nameof(model.CommercialRegistrationFile), "Commercial registration", true,
            model.CommercialRegistrationExpiry, nameof(model.CommercialRegistrationExpiry), uploads);
        CheckDocument(model.TaxCertificateFile, nameof(model.TaxCertificateFile), "Tax certificate", true,
            model.TaxCertificateExpiry, nameof(model.TaxCertificateExpiry), uploads);
        CheckDocument(model.OtherDocumentFile, nameof(model.OtherDocumentFile), "Other document", false, null, null, uploads);
        if (!ModelState.IsValid) return View(model);

        // The account stays inactive until the Admin approves the company.
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.ContactPerson,
            UserType = "Supplier",
            PhoneNumber = model.ContactPhone,
            IsActive = false,
        };
        var created = await _users.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        var stored = new List<(string Type, StoredFile File, DateTime? Expiry)>();
        try
        {
            foreach (var u in uploads)
            {
                await using var stream = u.File.OpenReadStream();
                stored.Add((u.Type, await _files.SaveAsync(stream, u.File.FileName, u.File.ContentType, HttpContext.RequestAborted), u.Expiry));
            }
        }
        catch (InvalidOperationException ex)
        {
            await _users.DeleteAsync(user);          // nobody is left with an account and no documents
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        await _users.AddToRoleAsync(user, Roles.Supplier);
        var profile = new Data.Entities.Identity.SupplierProfile
        {
            UserId = user.Id,
            CompanyName = model.CompanyName,
            TaxNumber = model.TaxNumber,
            CommercialRegNumber = model.CommercialRegNumber,
            Category = model.Category,
            Country = model.Country,
            City = model.City,
            Address = model.Address,
            Website = model.Website,
            ContactPerson = model.ContactPerson,
            ContactPhone = model.ContactPhone,
            Status = "Pending",
        };
        foreach (var d in stored)
            profile.SupplierDocuments.Add(new Data.Entities.Identity.SupplierDocument
            {
                DocType = d.Type,
                FilePath = d.File.RelativePath,
                ExpiryDate = d.Expiry,
                UploadedAt = DateTime.UtcNow,
            });
        _db.SupplierProfiles.Add(profile);
        await _db.SaveChangesAsync();

        await _audit.LogForUserAsync(null, "Registered", "ApplicationUser", user.Id,
            $"Supplier account registered; approval pending; {stored.Count} document(s).");

        await _notify.NotifyRoleAsync(Roles.Admin, "SupplierRegistered", "New supplier registration",
            $"{model.CompanyName} registered and is waiting for approval.", "/Admin/Suppliers");

        TempData["Success"] = "Thank you! Your registration was received. An Admin will review your company, and you can sign in once it is approved.";
        return RedirectToAction(nameof(Login));
    }

    // Checks one uploaded document (required or optional): type and size come from the UPLOAD_* settings in .env.
    private void CheckDocument(IFormFile? file, string field, string type, bool required, DateTime? expiry, string? expiryField,
        List<(string Type, IFormFile File, DateTime? Expiry)> uploads)
    {
        if (file is null || file.Length == 0)
        {
            if (required) ModelState.AddModelError(field, $"Please attach your {type.ToLowerInvariant()}.");
            return;
        }

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (string.IsNullOrWhiteSpace(extension) || !_upload.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(field, "This file type is not allowed. Allowed: " + string.Join(", ", _upload.AllowedExtensions) + ".");
            return;
        }
        if (file.Length > _upload.MaxFileSizeMb * 1024L * 1024L)
        {
            ModelState.AddModelError(field, $"The file is larger than {_upload.MaxFileSizeMb} MB.");
            return;
        }
        if (expiry is not null && expiry.Value.Date < DateTime.UtcNow.Date && expiryField is not null)
        {
            ModelState.AddModelError(expiryField, "This document has already expired.");
            return;
        }
        uploads.Add((type, file, expiry?.Date));
    }
}
