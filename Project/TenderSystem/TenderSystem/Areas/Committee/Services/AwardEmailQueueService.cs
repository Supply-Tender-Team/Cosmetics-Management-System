using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Email;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (TOP-02) - puts the supplier emails of a FINAL Top Committee decision in Wadea's email outbox.
//
//  - Awarded tender : one email to the winning supplier and one to every other supplier that submitted an offer.
//  - Rejected tender: one "closed without award" email to every supplier that submitted an offer.
//  - Retender Required: NO supplier email. The Sourcing Officer must review that result first (separate workflow).
//
// It sends nothing itself. EmailOutboxService only stores the email; Wadea's background worker sends it, retries
// failures and records the result (see Services/Email/EMAIL-OUTBOX.md). Each email has its own dedup key
// ("award-winner:{tender}:{supplier}" and so on), so running this again, for example from "Send missing
// notifications", never creates a second email.
//
// When it runs: only AFTER the decision is committed, and in the BACKGROUND (QueueInBackground), so the voter who
// casts the fourth vote never waits for an AI model. A failure here never changes or undoes the decision; it is
// written to the audit log and can be repeated safely.
//
// AI: the email text is drafted by AiAwardEmailService only when the AI is enabled AND runs on this computer
// (prices and supplier names must not leave the company). Otherwise, or when the AI answer fails its checks,
// the fixed text of AwardEmailFallbackTemplates is used.
//
// Not covered here: a tender awarded directly by the Sourcing Manager is not triggered from the voting flow;
// whoever finishes that decision must call QueueDecisionEmailsAsync / QueueInBackground for it.
public class AwardEmailQueueService
{
    private readonly ApplicationDbContext _db;
    private readonly AwardEmailPreparationService _prepare;
    private readonly AiAwardEmailService _aiEmail;
    private readonly AiGenerationService _aiGeneration;
    private readonly EmailOutboxService _outbox;
    private readonly AuditService _audit;
    private readonly IServiceScopeFactory _scopes;

    public AwardEmailQueueService(ApplicationDbContext db, AwardEmailPreparationService prepare,
        AiAwardEmailService aiEmail, AiGenerationService aiGeneration, EmailOutboxService outbox,
        AuditService audit, IServiceScopeFactory scopes)
    {
        _db = db;
        _prepare = prepare;
        _aiEmail = aiEmail;
        _aiGeneration = aiGeneration;
        _outbox = outbox;
        _audit = audit;
        _scopes = scopes;
    }

    /// <summary>
    /// Starts queueing in the background with its own scope and database connection, and returns at once.
    /// If the application stops before it finishes, "Send missing notifications" repeats it without duplicates.
    /// </summary>
    public void QueueInBackground(int tenderId)
    {
        var scopes = _scopes;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<AwardEmailQueueService>();
                await queue.QueueDecisionEmailsAsync(tenderId);
            }
            catch (Exception)
            {
                // QueueDecisionEmailsAsync already audits its own failures; nothing else can be done here.
            }
        });
    }

    /// <summary>
    /// Queues the supplier emails of the saved final decision. Returns false when something could not be queued.
    /// It never throws. Safe to run again.
    /// </summary>
    public async Task<bool> QueueDecisionEmailsAsync(int tenderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _db.Tenders.AsNoTracking()
                .Where(t => t.Id == tenderId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (status == Statuses.TenderStatus.Awarded) return await QueueAwardedAsync(tenderId, cancellationToken);
            if (status == Statuses.TenderStatus.Rejected) return await QueueRejectedAsync(tenderId, cancellationToken);

            // Retender Required (waits for the Sourcing Officer's review) or a tender without a final decision.
            return true;
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear();
            await TryAuditAsync("TopCommitteeEmailQueueFailed", tenderId, "The decision emails could not be queued; they can be queued again.");
            return false;
        }
    }

    // ------------------------------------------------------------------ Awarded

    private async Task<bool> QueueAwardedAsync(int tenderId, CancellationToken ct)
    {
        var prepared = await _prepare.PrepareAsync(tenderId, ct);
        if (!prepared.Success)
        {
            await TryAuditAsync("TopCommitteeEmailQueueFailed", tenderId, "The award emails could not be prepared because the saved award facts are not complete.");
            return false;
        }

        var useAi = _aiEmail.Enabled && _aiGeneration.IsLocal(AiGenerationService.Email);
        int created = 0, existing = 0, failed = 0;

        if (prepared.Winner is { } winner)
        {
            var content = await WinnerContentAsync(winner, prepared, useAi, ct);
            Count(await EnqueueAsync($"award-winner:{tenderId}:{winner.SupplierProfileId}", "AwardWinner",
                winner.RecipientEmail, content, tenderId, ct), ref created, ref existing, ref failed);
        }

        foreach (var supplier in prepared.Unsuccessful)
        {
            var content = await UnsuccessfulContentAsync(supplier, prepared, useAi, ct);
            Count(await EnqueueAsync($"award-unsuccessful:{tenderId}:{supplier.SupplierProfileId}", "AwardUnsuccessful",
                supplier.RecipientEmail, content, tenderId, ct), ref created, ref existing, ref failed);
        }

        var withoutEmail = (prepared.WinnerMissingEmail ? 1 : 0) + prepared.UnsuccessfulMissingEmail;
        await AuditSummaryAsync("Awarded", tenderId, created, existing, failed, withoutEmail);
        return failed == 0 && !prepared.WinnerMissingEmail;
    }

    private async Task<AwardEmailContent> WinnerContentAsync(WinningSupplierEmailPayload payload,
        AwardEmailPreparationResult prepared, bool useAi, CancellationToken ct)
    {
        if (useAi)
        {
            try
            {
                var draft = await _aiEmail.DraftAsync(AwardEmailRequestFactory.ForWinner(payload, prepared), ct);
                if (draft.Ok) return AwardEmailFallbackTemplates.FromPlainText(draft.Subject, draft.Body);
            }
            catch (Exception)
            {
                // Use the fixed text below.
            }
        }
        return AwardEmailFallbackTemplates.Winner(payload);
    }

    private async Task<AwardEmailContent> UnsuccessfulContentAsync(UnsuccessfulSupplierEmailPayload payload,
        AwardEmailPreparationResult prepared, bool useAi, CancellationToken ct)
    {
        if (useAi)
        {
            try
            {
                var draft = await _aiEmail.DraftAsync(AwardEmailRequestFactory.ForUnsuccessful(payload, prepared), ct);
                if (draft.Ok) return AwardEmailFallbackTemplates.FromPlainText(draft.Subject, draft.Body);
            }
            catch (Exception)
            {
                // Use the fixed text below.
            }
        }
        return AwardEmailFallbackTemplates.Unsuccessful(payload);
    }

    // ------------------------------------------------------------------ Rejected

    // Rejected emails have a fixed text (no AI): they only say the tender was closed without an award.
    private async Task<bool> QueueRejectedAsync(int tenderId, CancellationToken ct)
    {
        var prepared = await _prepare.PrepareRejectedAsync(tenderId, ct);
        if (!prepared.Success)
        {
            await TryAuditAsync("TopCommitteeEmailQueueFailed", tenderId, "The rejected-tender emails could not be prepared because the saved decision is not complete.");
            return false;
        }

        int created = 0, existing = 0, failed = 0;
        foreach (var supplier in prepared.Recipients)
        {
            Count(await EnqueueAsync($"tender-rejected:{tenderId}:{supplier.SupplierProfileId}", "TenderRejected",
                supplier.RecipientEmail, AwardEmailFallbackTemplates.Rejected(supplier), tenderId, ct),
                ref created, ref existing, ref failed);
        }

        await AuditSummaryAsync("Rejected", tenderId, created, existing, failed, prepared.MissingEmail);
        return failed == 0;
    }

    // ------------------------------------------------------------------ helpers

    private enum QueueOutcome { Created, AlreadyQueued, Failed }

    private async Task<QueueOutcome> EnqueueAsync(string dedupKey, string category, string toEmail,
        AwardEmailContent content, int tenderId, CancellationToken ct)
    {
        var queued = await _outbox.EnqueueAsync(new EmailOutboxRequest(
            dedupKey, category, toEmail, content.Subject, content.HtmlBody, "Tender", tenderId.ToString()), ct);

        if (queued.Error is not null) return QueueOutcome.Failed;
        return queued.Created ? QueueOutcome.Created : QueueOutcome.AlreadyQueued;
    }

    private static void Count(QueueOutcome outcome, ref int created, ref int existing, ref int failed)
    {
        if (outcome == QueueOutcome.Created) created++;
        else if (outcome == QueueOutcome.AlreadyQueued) existing++;
        else failed++;
    }

    // Counts only: never an address, a name or a price. Nothing is written when a repeat run found nothing new.
    private Task AuditSummaryAsync(string outcome, int tenderId, int created, int existing, int failed, int withoutEmail) =>
        created == 0 && failed == 0 && withoutEmail == 0
            ? Task.CompletedTask
            : TryAuditAsync("TopCommitteeEmailsQueued", tenderId,
                $"{outcome} decision emails: {created} queued, {existing} already queued, {failed} failed, {withoutEmail} supplier(s) without a usable email address.");

    private async Task TryAuditAsync(string action, int tenderId, string details)
    {
        try
        {
            await _audit.LogAsync(action, "Tender", tenderId.ToString(), details);
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear(); // the audit itself failed; nothing more can be done here
        }
    }
}
