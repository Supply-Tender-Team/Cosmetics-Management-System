using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (helping Dana with TOP-02) - Phase 2: notifications about the final decision.
//
// Called only AFTER the decision transaction is committed. It reads the saved decision again, so it never
// announces something that was not saved.
//
// Duplicates and recovery: every recipient is handled on its own, in a short transaction that locks the
// tender row and checks whether THIS recipient already has THIS notification (same type and same link, and
// every link contains the tender id). Notifications cannot be deleted in this project, so a missing one can
// be re-sent safely at any time ("Send missing notifications"), and two runs at the same time cannot both
// send it. One failure is audited and does not stop the others or undo the decision.
//
// Privacy: suppliers get only their own outcome (selected / not selected / closed without award).
// No votes, reasons, other suppliers or prices. The "blocked" notice never reveals the outcome.
public class CommitteeNotifier
{
    private const string DecisionType = "TopCommitteeDecision";
    private const string BlockedType = "TopCommitteeDecisionBlocked";

    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;

    public CommitteeNotifier(ApplicationDbContext db, NotificationService notifications, AuditService audit)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
    }

    /// <summary>Sends every decision notification that has not been sent yet. Safe to call again.</summary>
    public async Task<bool> SendDecisionNoticesAsync(int tenderId, List<string> memberIds)
    {
        List<PlannedNotice> notices;
        try
        {
            notices = await PlanDecisionNoticesAsync(tenderId, memberIds);
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear();
            await TryAuditFailureAsync(tenderId, "Decision notifications could not be prepared.");
            return false;
        }

        var allSaved = true;
        foreach (var notice in notices)
            if (!await SendOnceAsync(tenderId, notice)) allSaved = false;
        return allSaved;
    }

    /// <summary>
    /// Tells the Sourcing Manager (neutrally) that all votes are in but the decision cannot be recorded yet.
    /// Sent at most once per tender.
    /// </summary>
    public async Task SendBlockedNoticeAsync(int tenderId)
    {
        try
        {
            var tender = await _db.Tenders.AsNoTracking()
                .Where(t => t.Id == tenderId)
                .Select(t => new { t.TenderNumber, t.Title })
                .FirstOrDefaultAsync();
            var managerUserId = await LatestManagerUserIdAsync(tenderId);
            if (tender is null || string.IsNullOrWhiteSpace(managerUserId)) return;

            await SendOnceAsync(tenderId, new PlannedNotice(
                managerUserId, BlockedType,
                "Top Committee decision on hold",
                $"All Top Committee votes for tender {Label(tender.TenderNumber, tender.Title)} have been received, but the final decision cannot be recorded yet. Please review the tender, its evaluation report and recommended offer, and contact the Top Committee.",
                $"/Manager/Dashboard?tenderId={tenderId}"));
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear();
            await TryAuditFailureAsync(tenderId, "The decision-on-hold notification could not be prepared.");
        }
    }

    /// <summary>
    /// Sends an explicit handoff of the saved Top Committee result to the Sourcing Manager.
    /// The notification is idempotent, so pressing the button again does not create a duplicate.
    /// </summary>
    public async Task<bool> SendResultToManagerAsync(int tenderId, string committeeUserId)
    {
        var isCommitteeMember = await (from userRole in _db.UserRoles.AsNoTracking()
                                       join role in _db.Roles.AsNoTracking()
                                           on userRole.RoleId equals role.Id
                                       join user in _db.Users.AsNoTracking()
                                           on userRole.UserId equals user.Id
                                       where userRole.UserId == committeeUserId
                                             && role.Name == Roles.TopCommittee
                                             && user.IsActive
                                             && user.UserType == Statuses.UserTypes.Employee
                                             && !_db.UserRoles.Any(other => other.UserId == user.Id
                                                 && other.RoleId != role.Id)
                                       select userRole.UserId).AnyAsync();
        if (!isCommitteeMember) return false;

        var decision = await _db.TenderApprovals.AsNoTracking()
            .Where(approval => approval.TenderId == tenderId
                && approval.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderByDescending(approval => approval.DecidedAt)
            .ThenByDescending(approval => approval.Id)
            .Select(approval => new
            {
                approval.Decision,
                approval.Tender.TenderNumber,
                approval.Tender.Title,
                AwardedSupplier = approval.SelectedBid == null
                    ? null
                    : approval.SelectedBid.SupplierProfile.CompanyName
            })
            .FirstOrDefaultAsync();
        if (decision is null) return false;

        var managerUserId = await LatestManagerUserIdAsync(tenderId);
        if (string.IsNullOrWhiteSpace(managerUserId)) return false;

        var label = Label(decision.TenderNumber, decision.Title);
        var awarded = decision.Decision == Statuses.ApprovalDecision.Approved;
        var message = awarded
            ? $"The Top Committee recorded the final result for {label}: awarded to {decision.AwardedSupplier}. Review the result and forward it to the Sourcing Officer."
            : $"The Top Committee recorded the final result for {label}: the tender was rejected and closed. Review the result and forward it to the Sourcing Officer.";

        return await SendOnceAsync(tenderId, new PlannedNotice(
            managerUserId,
            "TopCommitteeResultForManager",
            "Top Committee result ready",
            message,
            $"/Manager/Decisions/TopResult?id={tenderId}"));
    }

    // Builds the list of decision notifications from the SAVED decision. Empty when nothing is saved.
    private async Task<List<PlannedNotice>> PlanDecisionNoticesAsync(int tenderId, List<string> memberIds)
    {
        var notices = new List<PlannedNotice>();

        var decision = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Decision })
            .FirstOrDefaultAsync();
        if (decision is null) return notices;

        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new
            {
                t.TenderNumber,
                t.Title,
                t.CreatedByUserId,
                t.RequisitionId,
                RequesterUserId = t.Requisition.RequestedByUserId,
                t.AwardedSupplierId,
                AwardedCompany = t.AwardedSupplier != null ? t.AwardedSupplier.CompanyName : null
            })
            .FirstAsync();

        var managerUserId = await LatestManagerUserIdAsync(tenderId);

        // Suppliers that took part (submitted offers), except the awarded one.
        var otherSupplierUserIds = await _db.Bids.AsNoTracking()
            .Where(b => b.TenderId == tenderId
                        && b.Status != Statuses.BidStatus.Draft
                        && b.Status != Statuses.BidStatus.Withdrawn
                        && b.SupplierProfileId != tender.AwardedSupplierId)
            .Select(b => b.SupplierProfile.UserId)
            .Distinct()
            .ToListAsync();

        var label = Label(tender.TenderNumber, tender.Title);
        var awarded = decision.Decision == Statuses.ApprovalDecision.Approved;

        // Sourcing Officer (tender creator) and the Sourcing Manager who forwarded it.
        // Earlier versions sent both of them this link; it is still checked so old notices are not re-sent.
        var legacySourcingLink = $"/Sourcing/Tenders?tenderId={tenderId}";
        if (awarded)
        {
            if (!string.IsNullOrWhiteSpace(tender.CreatedByUserId))
            {
                notices.Add(new PlannedNotice(tender.CreatedByUserId, DecisionType, "Tender awarded",
                    $"Tender {label} was awarded to {tender.AwardedCompany} by the Top Committee.",
                    legacySourcingLink, legacySourcingLink));
            }
            if (!string.IsNullOrWhiteSpace(managerUserId) && managerUserId != tender.CreatedByUserId)
            {
                notices.Add(new PlannedNotice(managerUserId, DecisionType, "Tender awarded",
                    $"Tender {label} was awarded to {tender.AwardedCompany} by the Top Committee.",
                    $"/Manager/Dashboard?tenderId={tenderId}", legacySourcingLink));
            }
        }
        else
        {
            // Manual Re-Tendering: the rejected tender stays closed; the committee never creates a new tender.
            if (!string.IsNullOrWhiteSpace(tender.CreatedByUserId))
            {
                notices.Add(new PlannedNotice(tender.CreatedByUserId, DecisionType, "Tender rejected by the Top Committee",
                    $"The Top Committee rejected tender {label} after all four members voted. This tender is closed and its history is preserved. " +
                    "No new tender was created automatically; authorized Sourcing staff may start a separate manual re-tender for the same purchase request.",
                    $"/Sourcing/Tenders/Details/{tenderId}",
                    legacySourcingLink));
            }
            if (!string.IsNullOrWhiteSpace(managerUserId) && managerUserId != tender.CreatedByUserId)
            {
                notices.Add(new PlannedNotice(managerUserId, DecisionType, "Tender rejected by the Top Committee",
                    $"The Top Committee rejected tender {label} after all four members voted. This tender is closed and its history is preserved. " +
                    "No new tender was created automatically; authorized Sourcing staff may start a separate manual re-tender for the same purchase request.",
                    $"/Manager/Dashboard?tenderId={tenderId}",
                    legacySourcingLink));
            }
        }

        // The original requester represents the department that raised the purchase request.
        if (!string.IsNullOrWhiteSpace(tender.RequesterUserId))
        {
            notices.Add(new PlannedNotice(tender.RequesterUserId, DecisionType,
                awarded ? "Your department's tender was awarded" : "Your department's tender was closed",
                awarded
                    ? $"Tender {label} for your department was awarded."
                    : $"Tender {label} for your department was rejected and closed by the Top Committee.",
                $"/Requester/Requests/Details/{tender.RequisitionId}"));
        }

        // Committee members.
        foreach (var userId in memberIds.Distinct())
        {
            notices.Add(new PlannedNotice(userId, DecisionType, "Final decision recorded",
                $"The final Top Committee decision for tender {label} was recorded.",
                $"/Committee/Votes/Result/{tenderId}"));
        }

        // For an award, the Sourcing Officer sends the supplier results from Evaluation Tracking.
        if (!awarded)
        {
            var supplierLink = $"/Supplier/Offers?tenderId={tenderId}";
            foreach (var userId in otherSupplierUserIds)
            {
                notices.Add(new PlannedNotice(userId, DecisionType, "Tender closed without award",
                    $"Tender {label} was closed without an award.", supplierLink));
            }
        }

        return notices;
    }

    // Sends one notification unless this recipient already has it. Runs in its own short transaction that
    // locks the tender row, so two runs at the same time cannot both send it.
    private async Task<bool> SendOnceAsync(int tenderId, PlannedNotice notice)
    {
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM Tenders WITH (UPDLOCK, HOLDLOCK) WHERE Id = {tenderId}");

            // A notice sent earlier with the legacy link counts as already sent (no duplicate after a link change).
            var legacyLink = notice.LegacyLink ?? notice.Link;
            var alreadySent = await _db.Notifications.AsNoTracking()
                .AnyAsync(n => n.UserId == notice.UserId && n.Type == notice.Type
                    && (n.Link == notice.Link || n.Link == legacyLink));
            if (!alreadySent)
                await _notifications.NotifyAsync(notice.UserId, notice.Type, notice.Title, Shorten(notice.Message, 500), notice.Link);

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception)
        {
            // Forget the unsaved notification so it is not retried by the next SaveChanges.
            _db.ChangeTracker.Clear();
            await TryAuditFailureAsync(tenderId, $"A {notice.Type} notification to user {notice.UserId} could not be saved; it can be re-sent later.");
            return false;
        }
    }

    private Task<string?> LatestManagerUserIdAsync(int tenderId) =>
        _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.Manager)
            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id)
            .Select(a => (string?)a.DecidedByUserId)
            .FirstOrDefaultAsync();

    private async Task TryAuditFailureAsync(int tenderId, string details)
    {
        try
        {
            await _audit.LogAsync("TopCommitteeNotificationFailed", "Tender", tenderId.ToString(), details);
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear(); // the audit itself failed; nothing more can be done here
        }
    }

    private static string Label(string number, string title) => $"{number} – {Shorten(title, 120)}";

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private sealed record PlannedNotice(string UserId, string Type, string Title, string Message, string Link,
        string? LegacyLink = null);
}
