using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Email;
using TenderSystem.Services.Files;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class SuppliersController : Controller
{
    // OWNER: Dev 3
    private readonly ApplicationDbContext _db;

    private readonly AuditService _audit;
    private readonly EmailService _email;
    private readonly NotificationService _notify;

    public SuppliersController(ApplicationDbContext db, AuditService audit, EmailService email, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _email = email;
        _notify = notify;
    }

    public async Task<IActionResult> Index(string? search, string status = "All")
    {
        search = search?.Trim();
        if (status is not ("Pending" or "Approved" or "Rejected" or "Suspended")) status = "All";

        var suppliers = _db.SupplierProfiles.AsNoTracking();
        var counts = await suppliers.GroupBy(s => s.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);

        if (!string.IsNullOrWhiteSpace(search))
            suppliers = suppliers.Where(s => s.CompanyName.Contains(search) || (s.User.Email != null && s.User.Email.Contains(search)));
        if (status != "All") suppliers = suppliers.Where(s => s.Status == status);

        var model = new SuppliersPageViewModel
        {
            Search = search,
            Status = status,
            TotalCount = counts.Values.Sum(),
            PendingCount = counts.GetValueOrDefault("Pending"),
            ApprovedCount = counts.GetValueOrDefault("Approved"),
            RejectedCount = counts.GetValueOrDefault("Rejected"),
            SuspendedCount = counts.GetValueOrDefault("Suspended"),
            Suppliers = await suppliers.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
                .Select(s => new SupplierRowViewModel
                {
                    Id = s.Id,
                    CompanyName = s.CompanyName,
                    Email = s.User.Email ?? "",
                    CreatedAt = s.CreatedAt,
                    Status = s.Status,
                    ReviewedAt = s.ReviewedAt,
                    ReviewedBy = s.ReviewedByUser != null ? s.ReviewedByUser.FullName : null
                }).ToListAsync()
        };
        return View(model);
    }

    private bool IsModalRequest() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task<IActionResult> Review(int id)
    {
        var model = await LoadReviewAsync(id);
        if (model is null) return NotFound();
        return IsModalRequest() ? PartialView("_ReviewPanel", model) : View(model);
    }

    private Task<SupplierReviewViewModel?> LoadReviewAsync(int id)
    {
        return _db.SupplierProfiles.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new SupplierReviewViewModel
            {
                Id = s.Id,
                CompanyName = s.CompanyName,
                Email = s.User.Email ?? "",
                TaxNumber = s.TaxNumber,
                CommercialRegNumber = s.CommercialRegNumber,
                Category = s.Category,
                Country = s.Country,
                City = s.City,
                Address = s.Address,
                ContactPerson = s.ContactPerson,
                ContactPhone = s.ContactPhone,
                Website = s.Website,
                Status = s.Status,
                StatusReason = s.StatusReason,
                CreatedAt = s.CreatedAt,
                ReviewedAt = s.ReviewedAt,
                ReviewedBy = s.ReviewedByUser != null ? s.ReviewedByUser.FullName : null,
                Documents = s.SupplierDocuments.OrderBy(d => d.DocType)
                    .Select(d => new SupplierDocumentRow
                    {
                        Id = d.Id,
                        DocType = d.DocType,
                        UploadedAt = d.UploadedAt,
                        ExpiryDate = d.ExpiryDate,
                        CanView = d.FilePath.EndsWith(".pdf") || d.FilePath.EndsWith(".png")
                            || d.FilePath.EndsWith(".jpg") || d.FilePath.EndsWith(".jpeg")
                    })
                    .ToList()
            }).FirstOrDefaultAsync();
    }

    // Supplier legal documents. The document must belong to the supplier being reviewed (id),
    // so a changed documentId in the URL cannot open another supplier's file. Files are read through
    // FileStorageService (no server path is exposed), every access is logged and PDFs are watermarked.
    // GET /Admin/Suppliers/DocumentView/5?documentId=12
    [HttpGet]
    public Task<IActionResult> DocumentView(int id, int documentId, [FromServices] IFileStorageService files,
        [FromServices] IWebHostEnvironment environment, CancellationToken cancellationToken)
        => SendDocumentAsync(id, documentId, true, files, environment, cancellationToken);

    // GET /Admin/Suppliers/DocumentDownload/5?documentId=12
    [HttpGet]
    public Task<IActionResult> DocumentDownload(int id, int documentId, [FromServices] IFileStorageService files,
        [FromServices] IWebHostEnvironment environment, CancellationToken cancellationToken)
        => SendDocumentAsync(id, documentId, false, files, environment, cancellationToken);

    private async Task<IActionResult> SendDocumentAsync(int supplierId, int documentId, bool inline,
        IFileStorageService files, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (supplierId <= 0 || documentId <= 0) return NotFound();
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(d => d.Id == documentId && d.SupplierProfileId == supplierId)
            .Select(d => new { d.DocType, d.FilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        var extension = Path.GetExtension(document.FilePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
        // Only safe types are shown inside the browser; everything else must be downloaded.
        if (inline && contentType is not ("application/pdf" or "image/png" or "image/jpeg")) return BadRequest();

        Stream? stream;
        try { stream = await files.OpenReadAsync(document.FilePath, cancellationToken); }
        catch (InvalidOperationException) { return NotFound(); }
        if (stream is null) return NotFound();

        await using (stream)
        {
            var accessedAt = DateTime.UtcNow;
            var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(_db, userId, "SupplierDocument", documentId,
                inline ? Statuses.DocumentAction.View : Statuses.DocumentAction.Download, accessedAt, cancellationToken);
            var fileName = document.DocType.Replace(' ', '-') + extension;

            byte[] content;
            if (contentType == "application/pdf")
            {
                var viewerName = history.FirstOrDefault()?.ViewerName ?? "Signed-in user";
                content = await DocumentWatermarkService.CreateWatermarkedCopyAsync(stream, fileName, viewerName,
                    accessedAt, environment.WebRootPath, cancellationToken, history);
            }
            else
            {
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, cancellationToken);
                content = copy.ToArray();
            }

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.XContentTypeOptions = "nosniff";
            // No file name = shown inline in the browser; with a file name = "attachment" download.
            return inline ? File(content, contentType) : File(content, contentType, fileName);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id) => ChangeStatusAsync(id, "Pending", "Approved", null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(int id, string? reason) => ChangeStatusAsync(id, "Pending", "Rejected", reason);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Suspend(int id, string? reason) => ChangeStatusAsync(id, "Approved", "Suspended", reason);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(int id)
    {
        if (id <= 0) return NotFound();
        var reviewerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (reviewerId is null) return Forbid();

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var supplier = await _db.SupplierProfiles.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.UserId, s.Status })
                .FirstOrDefaultAsync();

            if (supplier is null) return NotFound();
            if (supplier.Status != Statuses.SupplierStatus.Suspended)
            {
                TempData["SupplierError"] = "This supplier is no longer suspended. Review the latest details before trying again.";
                return RedirectToAction(nameof(Review), new { id });
            }

            var supplierRole = await (from userRole in _db.UserRoles.AsNoTracking()
                                      join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                      where userRole.UserId == supplier.UserId
                                      select role.Name).ToListAsync();
            if (supplierRole.Count != 1 || supplierRole[0] != Roles.Supplier) return Forbid();

            var changedProfile = await _db.SupplierProfiles
                .Where(s => s.Id == id && s.Status == Statuses.SupplierStatus.Suspended)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, Statuses.SupplierStatus.Approved)
                    .SetProperty(s => s.StatusReason, (string?)null)
                    .SetProperty(s => s.ReviewedByUserId, reviewerId)
                    .SetProperty(s => s.ReviewedAt, DateTime.UtcNow));
            var changedUser = changedProfile == 1
                ? await _db.Users.Where(u => u.Id == supplier.UserId && u.UserType == "Supplier" && !u.IsActive)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsActive, true))
                : 0;
            if (changedProfile != 1 || changedUser != 1)
            {
                TempData["SupplierError"] = "The supplier account changed or is inconsistent. No reactivation was saved.";
                return RedirectToAction(nameof(Review), new { id });
            }

            await _audit.LogAsync("StatusChanged", "SupplierProfile", id.ToString(),
                Statuses.SupplierStatus.Approved, Statuses.SupplierStatus.Suspended);
            await _notify.NotifyAsync(supplier.UserId, "SupplierStatus", "Your supplier account was reactivated",
                "You can sign in and use approved supplier features again.", "/Supplier/Dashboard");
            await transaction.CommitAsync();
            TempData["SupplierSuccess"] = "Supplier reactivated successfully.";
        }
        catch (Exception)
        {
            TempData["SupplierError"] = "The supplier could not be reactivated. No change was saved. Please try again.";
        }

        return RedirectToAction(nameof(Review), new { id });
    }

    private async Task<IActionResult> ChangeStatusAsync(int id, string currentStatus, string newStatus, string? reason)
    {
        if (id <= 0) return NotFound();

        reason = reason?.Trim();
        if (newStatus != "Approved" && (reason is null || reason.Length < 10 || reason.Length > 500))
        {
            TempData["SupplierError"] = "Provide a meaningful reason between 10 and 500 characters.";
            return RedirectToAction(nameof(Review), new { id });
        }

        var reviewerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (reviewerId is null) return Forbid();

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var supplier = await _db.SupplierProfiles.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.UserId, s.Status })
                .FirstOrDefaultAsync();

            if (supplier is null) return NotFound();
            if (supplier.Status != currentStatus)
            {
                TempData["SupplierError"] = "This supplier's status has changed. Review the latest details before trying again.";
                return RedirectToAction(nameof(Review), new { id });
            }

            var reviewedAt = DateTime.UtcNow;
            var changedProfile = await _db.SupplierProfiles
                .Where(s => s.Id == id && s.Status == currentStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, newStatus)
                    .SetProperty(s => s.StatusReason, newStatus == "Approved" ? null : reason)
                    .SetProperty(s => s.ReviewedByUserId, reviewerId)
                    .SetProperty(s => s.ReviewedAt, reviewedAt));

            var changedUser = changedProfile == 1
                ? await _db.Users.Where(u => u.Id == supplier.UserId && u.UserType == "Supplier"
                    && u.IsActive == (currentStatus == "Approved"))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsActive, newStatus == "Approved"))
                : 0;

            if (changedProfile != 1 || changedUser != 1)
            {
                TempData["SupplierError"] = "The supplier account changed or is inconsistent. No review action was saved.";
                return RedirectToAction(nameof(Review), new { id });
            }

            await transaction.CommitAsync();
            TempData["SupplierSuccess"] = $"Supplier {newStatus.ToLowerInvariant()} successfully.";

            // The review is saved. Audit, in-app notification and email to the supplier (FIX-01).
            try
            {
                var info = await _db.SupplierProfiles.AsNoTracking().Where(s => s.Id == id)
                    .Select(s => new { s.UserId, s.CompanyName, s.User.Email }).FirstAsync();
                await _audit.LogAsync("StatusChanged", "SupplierProfile", id.ToString(), newStatus, currentStatus);
                await _notify.NotifyAsync(info.UserId, "SupplierStatus", "Your registration was " + newStatus.ToLowerInvariant(),
                    reason ?? "You can now sign in.", "/Supplier/Dashboard");
                var body = newStatus switch
                {
                    "Approved" => AdminEmailTemplates.SupplierApproved(info.CompanyName),
                    "Rejected" => AdminEmailTemplates.SupplierRejected(info.CompanyName, reason),
                    _ => EmailTemplates.SupplierSuspended(info.CompanyName, reason!),
                };
                var subject = newStatus switch
                {
                    "Approved" => AdminEmailTemplates.SupplierApprovalSubject,
                    "Rejected" => AdminEmailTemplates.SupplierRejectionSubject,
                    _ => "Your supplier registration: " + newStatus,
                };
                var sent = await _email.SendAsync(info.Email!, subject, body);
                if (!sent && newStatus is ("Approved" or "Rejected"))
                    TempData["SupplierError"] = "The review was saved, but the notification email could not be delivered.";
            }
            catch (Exception)
            {
                TempData["SupplierError"] = "The review was saved, but the audit entry or the notification could not be written.";
            }
        }
        catch (Exception)
        {
            TempData["SupplierError"] = "The review could not be saved. Please try again.";
        }

        return RedirectToAction(nameof(Review), new { id });
    }
}
