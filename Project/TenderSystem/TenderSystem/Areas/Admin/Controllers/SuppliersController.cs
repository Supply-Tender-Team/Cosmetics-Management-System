using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Email;
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

    public async Task<IActionResult> Review(int id)
    {
        var model = await _db.SupplierProfiles.AsNoTracking().Where(s => s.Id == id)
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
                ReviewedBy = s.ReviewedByUser != null ? s.ReviewedByUser.FullName : null
            }).FirstOrDefaultAsync();
        if (model is null) return NotFound();
        return View(model);
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
                    "Approved" => EmailTemplates.SupplierApproved(info.CompanyName, _email.AbsoluteUrl("/Account/Login")),
                    "Rejected" => EmailTemplates.SupplierRejected(info.CompanyName, reason!),
                    _ => EmailTemplates.SupplierSuspended(info.CompanyName, reason!),
                };
                await _email.SendAsync(info.Email!, "Your supplier registration: " + newStatus, body);
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
