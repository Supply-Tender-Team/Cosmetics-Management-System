using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

public partial class EvaluationController
{
    private const string AwardNoticesSent = "SupplierAwardResultsSent";

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendSupplierResults(int tenderId, CancellationToken cancellationToken)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(officerId)) return Challenge();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT Id FROM Tenders WITH (UPDLOCK, HOLDLOCK) WHERE Id = {tenderId}", cancellationToken);

        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId && t.CreatedByUserId == officerId)
            .Select(t => new { t.Status, t.TenderNumber, t.AwardedSupplierId })
            .SingleOrDefaultAsync(cancellationToken);
        if (tender is null) return NotFound();
        if (tender.Status != Statuses.TenderStatus.Awarded || tender.AwardedSupplierId is null)
            return Conflict("Supplier results can only be sent after the tender is awarded.");

        if (await SupplierResultsSentAsync(tenderId, cancellationToken))
        {
            TempData["EvaluationTrackingMessage"] = "The supplier results have already been sent.";
            return RedirectToAction(nameof(Index), new { tenderId });
        }

        var recipients = await _db.Bids.AsNoTracking()
            .Where(b => b.TenderId == tenderId && b.SubmittedAt.HasValue
                && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
            .OrderByDescending(b => b.SubmittedAt)
            .ThenByDescending(b => b.Id)
            .Select(b => new { b.Id, b.SupplierProfileId, b.SupplierProfile.UserId })
            .ToListAsync(cancellationToken);
        recipients = recipients.DistinctBy(b => b.SupplierProfileId).ToList();
        var winner = recipients.SingleOrDefault(r => r.SupplierProfileId == tender.AwardedSupplierId);
        if (winner is null)
            return Conflict("The recorded winner has no submitted offer for this tender.");

        foreach (var recipient in recipients)
        {
            var won = recipient == winner;
            await _notifications.NotifyAsync(
                recipient.UserId,
                won ? "SupplierTenderAwarded" : "SupplierTenderNotSelected",
                won ? "Congratulations! Your offer was selected" : "Thank you for your offer",
                won
                    ? $"Congratulations! Your offer was selected for tender {tender.TenderNumber}. Thank you for participating. Our sourcing team will contact you about the next steps."
                    : $"Thank you for submitting an offer for tender {tender.TenderNumber}. Your offer was not selected this time. We appreciate the time you put into it and hope to see you in future tenders.",
                $"/Supplier/OfferDetails/Index/{recipient.Id}");
        }

        await _audit.LogForUserAsync(officerId, AwardNoticesSent, "Tender", tenderId.ToString(),
            $"Award results sent to {recipients.Count} suppliers.");
        await transaction.CommitAsync(cancellationToken);

        TempData["EvaluationTrackingMessage"] = $"Results sent to {recipients.Count} suppliers.";
        return RedirectToAction(nameof(Index), new { tenderId });
    }

    private Task<bool> SupplierResultsSentAsync(int tenderId, CancellationToken cancellationToken)
    {
        var entityId = tenderId.ToString();
        return _db.AuditLogs.AsNoTracking().AnyAsync(log => log.Action == AwardNoticesSent
            && log.EntityName == "Tender" && log.EntityId == entityId, cancellationToken);
    }
}
