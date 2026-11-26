using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class InvitationsController : Controller
{
    // OWNER: Dev 5

    private const string PendingStatus = Statuses.InvitationStatus.Pending;
    private const string AcceptedStatus = Statuses.InvitationStatus.Accepted;
    private const string DeclinedStatus = Statuses.InvitationStatus.Declined;
    private const string ExpiredStatus = Statuses.InvitationStatus.Expired;

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;

    public InvitationsController(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var supplierProfileId = await _db.SupplierProfiles.AsNoTracking()
            .Where(profile => profile.UserId == supplierUserId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierProfileId is null)
            return View(Array.Empty<SupplierInvitationRow>());

        var now = DateTime.UtcNow;
        var expiredInvitations = await _db.Invitations
            .Where(invitation => invitation.SupplierProfileId == supplierProfileId
                && invitation.Status == PendingStatus
                && invitation.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        if (expiredInvitations.Count > 0)
        {
            foreach (var invitation in expiredInvitations)
                invitation.Status = ExpiredStatus;
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var invitation in expiredInvitations)
                await _audit.LogAsync("Expired", "Invitation", invitation.Id.ToString(), "Supplier invitation expired.");
        }

        var recentInvitations = await _db.Invitations.AsNoTracking()
            .Where(invitation => invitation.SupplierProfileId == supplierProfileId)
            .OrderByDescending(invitation => invitation.SentAt)
            .Select(invitation => new SupplierInvitationRow
            {
                Id = invitation.Id,
                TenderId = invitation.TenderId,
                TenderNumber = invitation.Tender.TenderNumber,
                TenderTitle = invitation.Tender.Title,
                Status = invitation.Status,
                ExpiresAt = invitation.ExpiresAt,
                CanRespond = invitation.Status == PendingStatus && invitation.ExpiresAt > now
            })
            .ToListAsync(cancellationToken);
        return View(recentInvitations);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(RespondToInvitationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "The invitation response is not valid.";
            return RedirectToAction(nameof(Index));
        }

        return await RespondAsync(model.InvitationId, AcceptedStatus, null, cancellationToken);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(RespondToInvitationViewModel model, CancellationToken cancellationToken)
    {
        model.DeclineReason = model.DeclineReason?.Trim() ?? string.Empty;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.DeclineReason))
        {
            TempData["Error"] = "Enter a reason for declining the invitation (up to 500 characters).";
            return RedirectToAction(nameof(Index));
        }

        return await RespondAsync(model.InvitationId, DeclinedStatus, model.DeclineReason?.Trim(), cancellationToken);
    }

    private async Task<IActionResult> RespondAsync(int invitationId, string responseStatus, string? declineReason,
        CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        var supplierProfileId = await _db.SupplierProfiles.AsNoTracking()
            .Where(profile => profile.UserId == supplierUserId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierProfileId is null) return Forbid();

        var invitation = await _db.Invitations
            .SingleOrDefaultAsync(invitation => invitation.Id == invitationId
                && invitation.SupplierProfileId == supplierProfileId, cancellationToken);
        if (invitation is null) return NotFound();
        if (invitation.Status == ExpiredStatus)
        {
            TempData["Error"] = "This invitation has expired.";
            return RedirectToAction(nameof(Index));
        }
        if (invitation.Status != PendingStatus)
        {
            TempData["Error"] = "This invitation has already been answered.";
            return RedirectToAction(nameof(Index));
        }

        var now = DateTime.UtcNow;
        if (invitation.ExpiresAt <= now)
        {
            invitation.Status = ExpiredStatus;
            invitation.RespondedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Expired", "Invitation", invitation.Id.ToString(), "Supplier invitation expired.");
            TempData["Error"] = "This invitation has expired.";
            return RedirectToAction(nameof(Index));
        }

        invitation.Status = responseStatus;
        invitation.RespondedAt = now;
        invitation.DeclineReason = responseStatus == DeclinedStatus ? declineReason : null;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(responseStatus, "Invitation", invitation.Id.ToString(), $"Supplier responded to tender {invitation.TenderId} invitation.");
        TempData["Success"] = responseStatus == AcceptedStatus
            ? "The invitation was accepted. You can now prepare your offer."
            : "The invitation was declined.";
        if (responseStatus == AcceptedStatus)
            return RedirectToAction("Details", "Tenders", new { area = "Supplier", id = invitation.TenderId });

        return RedirectToAction(nameof(Index));
    }
}
