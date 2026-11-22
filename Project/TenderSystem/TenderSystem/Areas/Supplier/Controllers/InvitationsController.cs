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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var supplierId = await _db.SupplierProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierId is null)
            return View(Array.Empty<SupplierInvitationRow>());

        var now = DateTime.UtcNow;
        var expiredInvitations = await _db.Invitations
            .Where(x => x.SupplierProfileId == supplierId
                && x.Status == PendingStatus
                && x.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        if (expiredInvitations.Count > 0)
        {
            foreach (var invitation in expiredInvitations)
                invitation.Status = ExpiredStatus;
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var invitation in expiredInvitations)
                await _audit.LogAsync("Expired", "Invitation", invitation.Id.ToString(), "Supplier invitation expired.");
        }

        var invitations = await _db.Invitations.AsNoTracking()
            .Where(x => x.SupplierProfileId == supplierId)
            .OrderByDescending(x => x.SentAt)
            .Select(x => new SupplierInvitationRow
            {
                Id = x.Id,
                TenderId = x.TenderId,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                ExpiresAt = x.ExpiresAt,
                CanRespond = x.Status == PendingStatus && x.ExpiresAt > now
            })
            .ToListAsync(cancellationToken);
        return View(invitations);
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

    private async Task<IActionResult> RespondAsync(int invitationId, string response, string? declineReason,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var supplierId = await _db.SupplierProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierId is null) return Forbid();

        var invitation = await _db.Invitations
            .SingleOrDefaultAsync(x => x.Id == invitationId && x.SupplierProfileId == supplierId, cancellationToken);
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

        invitation.Status = response;
        invitation.RespondedAt = now;
        invitation.DeclineReason = response == DeclinedStatus ? declineReason : null;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(response, "Invitation", invitation.Id.ToString(), $"Supplier responded to tender {invitation.TenderId} invitation.");
        TempData["Success"] = response == AcceptedStatus
            ? "The invitation was accepted. You can now prepare your offer."
            : "The invitation was declined.";
        if (response == AcceptedStatus)
            return RedirectToAction("Details", "Tenders", new { area = "Supplier", id = invitation.TenderId });

        return RedirectToAction(nameof(Index));
    }
}
