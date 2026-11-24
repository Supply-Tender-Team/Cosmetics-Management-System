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

    // Supplier result types: the same names the Notifications page already knows (icon and label).
    private const string SupplierAwardedType = "SupplierTenderAwarded";
    private const string SupplierNotSelectedType = "SupplierTenderNotSelected";

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
            ? $"The Top Committee recorded the final result for {label}: awarded to {decision.AwardedSupplier}. The Sourcing Officer is notified directly."
            : decision.Decision == Statuses.ApprovalDecision.RetenderRequired
                ? $"The Top Committee recorded the final result for {label}: retender required. The current tender is closed. The Sourcing Officer is notified directly; supplier notices must wait for officer review."
                : $"The Top Committee recorded the final result for {label}: the tender was rejected and closed. The Sourcing Officer is notified directly.";

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
            .Select(a => new { a.Decision, a.SelectedBidId })
            .FirstOrDefaultAsync();
        if (decision is null) return notices;

        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new
            {
                t.Status,
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
        else if (decision.Decision == Statuses.ApprovalDecision.RetenderRequired)
        {
            // The officer reviews and releases supplier notices in a separate workflow.
            if (!string.IsNullOrWhiteSpace(tender.CreatedByUserId))
            {
                notices.Add(new PlannedNotice(tender.CreatedByUserId, DecisionType, "Retender required by the Top Committee",
                    $"The Top Committee requires a retender for {label}. The current tender is closed. Review the result before any supplier notices; create any successor tender manually.",
                    $"/Sourcing/Tenders/Details/{tenderId}", legacySourcingLink));
            }
            if (!string.IsNullOrWhiteSpace(managerUserId) && managerUserId != tender.CreatedByUserId)
            {
                notices.Add(new PlannedNotice(managerUserId, DecisionType, "Retender required by the Top Committee",
                    $"The Top Committee requires a retender for {label}. The current tender is closed. The Sourcing Officer is notified directly; supplier notices must wait for officer review.",
                    $"/Manager/Dashboard?tenderId={tenderId}", legacySourcingLink));
            }
        }
        else
        {
            // Rejection is final: the tender is permanently closed and no successor is created.
            if (!string.IsNullOrWhiteSpace(tender.CreatedByUserId))
            {
                notices.Add(new PlannedNotice(tender.CreatedByUserId, DecisionType, "Tender rejected by the Top Committee",
                    $"The Top Committee has rejected tender {label}. The decision is final, and the tender is permanently closed. No successor tender was created.",
                    $"/Sourcing/Tenders/Details/{tenderId}",
                    legacySourcingLink));
            }
            if (!string.IsNullOrWhiteSpace(managerUserId) && managerUserId != tender.CreatedByUserId)
            {
                notices.Add(new PlannedNotice(managerUserId, DecisionType, "Tender rejected by the Top Committee",
                    $"The Top Committee has rejected tender {label}. The decision is final, and the tender is permanently closed. No successor tender was created.",
                    $"/Manager/Dashboard?tenderId={tenderId}",
                    legacySourcingLink));
            }
        }

        // The original requester represents the department that raised the purchase request.
        if (!string.IsNullOrWhiteSpace(tender.RequesterUserId))
        {
            notices.Add(new PlannedNotice(tender.RequesterUserId, DecisionType,
                awarded ? "Your department's tender was awarded"
                    : decision.Decision == Statuses.ApprovalDecision.RetenderRequired ? "Your department's tender requires a retender" : "Your department's tender was closed",
                awarded
                    ? $"Tender {label} for your department was awarded."
                    : decision.Decision == Statuses.ApprovalDecision.RetenderRequired
                        ? $"Tender {label} for your department requires a retender. The current tender is closed; Sourcing will review the next steps."
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

        // Award: the winner and every other participating supplier are notified automatically (no officer release).
        // Only when the saved tender really is Awarded to a supplier; the decision row alone is not enough.
        if (awarded && tender.Status == Statuses.TenderStatus.Awarded && tender.AwardedSupplierId is int awardedSupplierId)
            notices.AddRange(await PlanAwardedSupplierNoticesAsync(tenderId, tender.TenderNumber, awardedSupplierId, decision.SelectedBidId));

        if (decision.Decision == Statuses.ApprovalDecision.Rejected)
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

    // In-app notices for an AWARDED tender: one for the winning supplier and one for each other participant.
    //  - Participants: submitted, non-withdrawn offers (same rule as the supplier emails), one per supplier.
    //  - Each notice links to that supplier's OWN offer; the link and type are the same as the legacy manual
    //    release used, so a notice that already exists is never created twice (SendOnceAsync checks them).
    //  - The text of a non-selection notice contains no winner, price, guarantee, score or ranking.
    //  - A user who owns more than one participating supplier profile gets one notice only (the winner's
    //    outcome wins), so one account never receives both "selected" and "not selected".
    // Shared mailboxes: the email rule skips a second supplier on the same address because one mailbox must not
    // get two emails. In-app notices go to separate portal accounts, so each account still gets its own result.
    private async Task<List<PlannedNotice>> PlanAwardedSupplierNoticesAsync(
        int tenderId, string tenderNumber, int awardedSupplierId, int? selectedBidId)
    {
        var notices = new List<PlannedNotice>();

        var offers = await _db.Bids.AsNoTracking()
            .Where(b => b.TenderId == tenderId && b.SubmittedAt.HasValue
                        && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
            .OrderByDescending(b => b.SubmittedAt).ThenByDescending(b => b.Id)
            .Select(b => new { b.Id, b.SupplierProfileId, b.SupplierProfile.UserId })
            .ToListAsync();

        // The winner's own offer: the one the decision selected, otherwise the latest submitted one.
        var winnerOffer = offers.FirstOrDefault(o => o.SupplierProfileId == awardedSupplierId && o.Id == selectedBidId)
                          ?? offers.FirstOrDefault(o => o.SupplierProfileId == awardedSupplierId);
        if (winnerOffer is null) return notices; // no submitted offer of the winner: nothing safe to announce

        var notifiedUsers = new HashSet<string>(StringComparer.Ordinal) { winnerOffer.UserId };
        notices.Add(new PlannedNotice(winnerOffer.UserId, SupplierAwardedType,
            "Congratulations! Your offer was selected",
            $"Congratulations! Your offer was selected for tender {tenderNumber}. Thank you for participating. Our sourcing team will contact you about the next steps.",
            OfferLink(winnerOffer.Id)));

        // Latest submitted offer of every other supplier (offers are already newest first).
        foreach (var offer in offers.Where(o => o.SupplierProfileId != awardedSupplierId)
                     .DistinctBy(o => o.SupplierProfileId))
        {
            if (!notifiedUsers.Add(offer.UserId)) continue;
            notices.Add(new PlannedNotice(offer.UserId, SupplierNotSelectedType,
                "Thank you for your offer",
                $"Thank you for submitting an offer for tender {tenderNumber}. Your offer was not selected this time. We appreciate the time you put into it and hope to see you in future tenders.",
                OfferLink(offer.Id)));
        }

        return notices;
    }

    private static string OfferLink(int bidId) => $"/Supplier/OfferDetails/Index/{bidId}";

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
