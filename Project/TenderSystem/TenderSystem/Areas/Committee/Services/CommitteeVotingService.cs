using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (helping Dana with TOP-02) - Top Committee voting and final decision.
//
// Rules (approved):
//  - The Top Committee is fixed: exactly 4 eligible accounts with the TopCommittee role.
//  - One voting round per tender: each member votes once (Approve or Reject; Reject needs a reason).
//  - Voting is allowed only for a tender that the Sourcing Manager forwarded with a valid report.
//  - An unresolved clarification sent by a committee member pauses voting and the decision (votes are kept).
//  - After the fourth vote the decision is recorded in the SAME transaction (CommitteeDecisionService).
//    If it cannot be recorded (for example the supplier was suspended), the vote is still saved, the
//    reason is shown, and a member can retry later. A false decision is never saved.
//  - Other members' votes are never shown before the decision; only "n of 4" and the member's own vote.
//
// Concurrency: the database reads with READ COMMITTED SNAPSHOT, so a plain "has he voted?" check takes
// no locks. Every vote and every retry therefore runs in a transaction that first locks the tender row
// with UPDLOCK, HOLDLOCK. All votes and decisions for the same tender then run one after another.
public class CommitteeVotingService
{
    public const int RequiredMembers = 4;
    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 1000;

    private const string CommitUncertainMessage =
        "We could not confirm that your action was saved. Refresh this page to check before trying again.";

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CommitteeDecisionService _decision;
    private readonly CommitteeNotifier _notifier;

    public CommitteeVotingService(ApplicationDbContext db, AuditService audit,
        CommitteeDecisionService decision, CommitteeNotifier notifier)
    {
        _db = db;
        _audit = audit;
        _decision = decision;
        _notifier = notifier;
    }

    /// <summary>
    /// The eligible committee accounts: TopCommittee role only (no other role), active employee,
    /// not locked out. Voting and decisions are allowed only when there are exactly four.
    /// </summary>
    public async Task<List<string>> GetEligibleMemberIdsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        return await (from userRole in _db.UserRoles
                      join role in _db.Roles on userRole.RoleId equals role.Id
                      join user in _db.Users on userRole.UserId equals user.Id
                      where role.Name == Roles.TopCommittee
                            && user.IsActive
                            && user.UserType == Statuses.UserTypes.Employee
                            && (user.LockoutEnd == null || user.LockoutEnd <= now)
                            && !_db.UserRoles.Any(other => other.UserId == user.Id && other.RoleId != role.Id)
                      select user.Id)
                     .Distinct()
                     .ToListAsync();
    }

    // ------------------------------------------------------------------ voting list (read-only)

    /// <summary>Most recent completed decisions shown on the list (older ones are reached from History later).</summary>
    public const int DecidedListSize = 50;

    /// <summary>
    /// The voting list for /Committee/Votes. Returns null when the user is not a CURRENT eligible member
    /// (re-checked in the database). Filtering and counting happen in SQL; no votes, reasons or prices.
    /// </summary>
    public async Task<VotingListViewModel?> GetVotingListAsync(string userId)
    {
        var members = await GetEligibleMemberIdsAsync();
        if (!members.Contains(userId)) return null;

        // Accounts whose unresolved clarification pauses voting: Top Committee accounts (current or not).
        var committeeAccountIds = await (from userRole in _db.UserRoles
                                         join role in _db.Roles on userRole.RoleId equals role.Id
                                         where role.Name == Roles.TopCommittee
                                         select userRole.UserId).ToListAsync();

        // Tenders waiting for the committee, newest first, without a recorded committee decision.
        var awaiting = await _db.Tenders.AsNoTracking()
            .Where(t => t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                        && t.RequiresTopCommittee
                        && !t.TenderApprovals.Any(a => a.Level == Statuses.ApprovalLevel.TopCommittee))
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new AwaitingTenderItem
            {
                TenderId = t.Id,
                TenderNumber = t.TenderNumber,
                Title = t.Title,
                VotesReceived = t.TopCommitteeVotes.Count(v => members.Contains(v.MemberUserId)),
                IVoted = t.TopCommitteeVotes.Any(v => v.MemberUserId == userId),
                // Same rule as CheckTenderAsync: unresolved clarification from a committee account or a voter.
                PausedByClarification = t.Clarifications.Any(c =>
                    c.Status != Statuses.ClarificationStatus.Resolved
                    && (committeeAccountIds.Contains(c.SenderUserId)
                        || t.TopCommitteeVotes.Any(v => v.MemberUserId == c.SenderUserId)))
            })
            .ToListAsync();

        // Recorded committee decisions, newest first. Only the outcome is shown here.
        var decided = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id)
            .Take(DecidedListSize)
            .Select(a => new DecidedTenderItem
            {
                TenderId = a.TenderId,
                TenderNumber = a.Tender.TenderNumber,
                Title = a.Tender.Title,
                Awarded = a.Decision == Statuses.ApprovalDecision.Approved,
                DecidedAt = a.DecidedAt
            })
            .ToListAsync();

        return new VotingListViewModel
        {
            EligibleMembers = members.Count,
            Awaiting = awaiting,
            Decided = decided
        };
    }

    // ------------------------------------------------------------------ status page (read-only)

    /// <summary>Read-only status for the vote page. Returns null when the tender does not exist.</summary>
    public async Task<VotingStatusViewModel?> GetStatusAsync(int tenderId, string userId)
    {
        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new { t.Id, t.TenderNumber, t.Title })
            .FirstOrDefaultAsync();
        if (tender is null) return null;

        var members = await GetEligibleMemberIdsAsync();
        var check = await CheckTenderAsync(tenderId);

        var votesReceived = await _db.TopCommitteeVotes.AsNoTracking()
            .CountAsync(v => v.TenderId == tenderId && members.Contains(v.MemberUserId));
        var myVote = await _db.TopCommitteeVotes.AsNoTracking()
            .Where(v => v.TenderId == tenderId && v.MemberUserId == userId)
            .Select(v => new { v.Vote, v.Comment, v.VotedAt })
            .FirstOrDefaultAsync();
        var decision = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Decision })
            .FirstOrDefaultAsync();

        var isMember = members.Contains(userId);
        // Only CURRENT committee members (checked in the database) see the recommended supplier and price.
        var canSeeRecommendation = isMember;

        var status = new VotingStatusViewModel
        {
            TenderId = tender.Id,
            TenderNumber = tender.TenderNumber,
            TenderTitle = tender.Title,
            RecommendedSupplier = canSeeRecommendation ? check.RecommendedSupplier : null,
            RecommendedPrice = canSeeRecommendation ? check.RecommendedPrice : null,
            RecommendedCurrency = canSeeRecommendation ? check.RecommendedCurrency : null,
            EligibleMembers = members.Count,
            IsMember = isMember,
            VotesReceived = votesReceived,
            MyVote = myVote?.Vote,
            MyComment = myVote?.Comment,
            MyVotedAt = myVote?.VotedAt,
            IsFinalized = decision is not null,
            Outcome = decision?.Decision
        };

        // The first reason that blocks this user from voting (null = the user can vote now).
        status.BlockReason =
            (status.IsFinalized ? "The final Top Committee decision for this tender has been recorded." : null)
            ?? check.BlockReason
            ?? (members.Count != RequiredMembers
                ? $"The Top Committee must have exactly {RequiredMembers} active members (currently {members.Count}). Ask the Admin to fix the committee accounts."
                : null)
            ?? (!isMember ? "Your account is not one of the four active Top Committee members." : null)
            ?? (myVote is not null ? "You have already voted on this tender." : null);

        // All four votes are in but no decision is saved: explain why and allow a retry.
        if (!status.IsFinalized && members.Count == RequiredMembers && votesReceived == RequiredMembers && isMember)
        {
            string? reason = check.BlockReason;
            if (reason is null)
            {
                var plan = await _decision.EvaluateAsync(tenderId, members, check);
                reason = plan.Kind == DecisionKind.Blocked ? plan.Message : null;
            }
            status.DecisionBlockedReason = reason ?? "All four votes are in, but the final decision has not been recorded yet.";
            status.CanRetryFinalize = true;
        }

        return status;
    }

    // ------------------------------------------------------------------ casting a vote

    /// <summary>
    /// Records one vote; after the fourth vote the decision is recorded in the same transaction.
    /// Only the tender id, the vote and the reason come from the browser; everything else is read here.
    /// </summary>
    public async Task<VoteResult> CastVoteAsync(int tenderId, string userId, string? vote, string? reason)
    {
        vote = vote?.Trim();
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (vote != Statuses.VoteType.Approve && vote != Statuses.VoteType.Reject)
            return VoteResult.Fail("Choose Approve or Reject.");
        if (vote == Statuses.VoteType.Reject && (reason is null || reason.Length < ReasonMinLength))
            return VoteResult.Fail($"A reason of at least {ReasonMinLength} characters is required to reject.");
        if (reason is not null && reason.Length > ReasonMaxLength)
            return VoteResult.Fail($"The reason can be at most {ReasonMaxLength} characters.");

        List<string> members;
        DecisionPlan? plan = null;
        int votesReceived;

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            await LockTenderAsync(tenderId);

            members = await GetEligibleMemberIdsAsync();
            if (members.Count != RequiredMembers)
                return VoteResult.Fail($"Voting is not possible: the Top Committee must have exactly {RequiredMembers} active members (currently {members.Count}).");
            if (!members.Contains(userId))
                return VoteResult.Fail("Your account is not one of the four active Top Committee members.");

            var check = await CheckTenderAsync(tenderId);
            if (check.BlockReason is not null) return VoteResult.Fail(check.BlockReason);

            var alreadyVoted = await _db.TopCommitteeVotes
                .AnyAsync(v => v.TenderId == tenderId && v.MemberUserId == userId);
            if (alreadyVoted) return VoteResult.Fail("You have already voted on this tender. Votes cannot be changed.");

            var newVote = new TopCommitteeVote
            {
                TenderId = tenderId,
                MemberUserId = userId,
                SelectedBidId = check.RecommendedBidId, // from the report, never from the browser
                Vote = vote,
                Comment = reason,
                VotedAt = DateTime.UtcNow
            };
            _db.TopCommitteeVotes.Add(newVote);
            await _db.SaveChangesAsync();

            votesReceived = await _db.TopCommitteeVotes
                .CountAsync(v => v.TenderId == tenderId && members.Contains(v.MemberUserId));

            // The audit entry does not contain the vote value, so it never reveals how a member voted.
            await _audit.LogAsync("TopCommitteeVoteCast", "Tender", tenderId.ToString(),
                $"Top Committee vote #{newVote.Id} recorded ({votesReceived} of {RequiredMembers}).");

            // The fourth vote: record the decision in this same transaction (or the reason it is blocked).
            if (votesReceived == RequiredMembers)
                plan = await _decision.TryFinalizeAsync(tenderId, userId, members, check);

            if (!await TryCommitAsync(transaction))
                return VoteResult.Fail(CommitUncertainMessage);
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            // Includes deadlocks and lock timeouts. The transaction is rolled back; nothing was saved.
            _db.ChangeTracker.Clear();
            return VoteResult.Fail("Your vote could not be saved right now. Please try again.");
        }

        if (plan?.Kind == DecisionKind.Finalized)
        {
            await _notifier.SendDecisionNoticesAsync(tenderId, members);
            return VoteResult.Ok("Your vote was recorded. " + plan.Message);
        }
        if (plan?.Kind == DecisionKind.Blocked)
        {
            // Neutral notice to the Sourcing Manager, sent at most once per tender (only from the fourth vote).
            await _notifier.SendBlockedNoticeAsync(tenderId);
            return VoteResult.Ok("Your vote was recorded. " + plan.Message);
        }

        return VoteResult.Ok($"Your vote was recorded ({votesReceived} of {RequiredMembers} votes received).");
    }

    // ------------------------------------------------------------------ retrying the final decision

    /// <summary>
    /// Tries again to record the final decision after all four votes (for example after the supplier
    /// was re-approved or a clarification was resolved). Same lock and checks as voting.
    /// </summary>
    public async Task<VoteResult> RetryFinalizeAsync(int tenderId, string userId)
    {
        List<string> members;
        DecisionPlan? plan = null;
        var alreadyDecided = false;

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            await LockTenderAsync(tenderId);

            members = await GetEligibleMemberIdsAsync();
            if (members.Count != RequiredMembers)
                return VoteResult.Fail($"The Top Committee must have exactly {RequiredMembers} active members (currently {members.Count}).");
            if (!members.Contains(userId))
                return VoteResult.Fail("Your account is not one of the four active Top Committee members.");

            alreadyDecided = await _db.TenderApprovals
                .AnyAsync(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee);

            if (!alreadyDecided)
            {
                var check = await CheckTenderAsync(tenderId);
                if (check.BlockReason is not null) return VoteResult.Fail(check.BlockReason);

                plan = await _decision.TryFinalizeAsync(tenderId, userId, members, check);

                // Commit also when blocked: the (single) "decision blocked" audit entry is kept.
                if (!await TryCommitAsync(transaction))
                    return VoteResult.Fail(CommitUncertainMessage);
            }
            // When the decision already exists nothing was changed; the transaction is simply
            // rolled back when it is disposed here, and the lock is released.
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            _db.ChangeTracker.Clear();
            return VoteResult.Fail("The final decision could not be recorded right now. Please try again.");
        }

        if (alreadyDecided)
        {
            // The decision may have been saved by a commit whose confirmation failed: recover any
            // notification that was not sent (the notifier skips the ones already sent).
            await _notifier.SendDecisionNoticesAsync(tenderId, members);
            return VoteResult.Ok("The final decision for this tender has already been recorded. Any missing notifications were sent.");
        }

        return plan!.Kind switch
        {
            DecisionKind.Finalized => await NotifyAndReturnAsync(tenderId, members, plan),
            DecisionKind.NotReady => VoteResult.Fail("Not all four votes have been received yet."),
            _ => VoteResult.Fail(plan.Message)
        };
    }

    private async Task<VoteResult> NotifyAndReturnAsync(int tenderId, List<string> members, DecisionPlan plan)
    {
        await _notifier.SendDecisionNoticesAsync(tenderId, members);
        return VoteResult.Ok(plan.Message);
    }

    // ------------------------------------------------------------------ recovering missed notifications

    /// <summary>
    /// Re-sends any decision notification that was not delivered (for example after a failed save).
    /// Current committee members only, and only after the decision is recorded. Recipients who already
    /// have the notification are skipped, so pressing it twice never creates duplicates.
    /// </summary>
    public async Task<VoteResult> ResendDecisionNoticesAsync(int tenderId, string userId)
    {
        var members = await GetEligibleMemberIdsAsync();
        if (!members.Contains(userId))
            return VoteResult.Fail("Your account is not one of the four active Top Committee members.");

        var decided = await _db.TenderApprovals.AsNoTracking()
            .AnyAsync(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee);
        if (!decided) return VoteResult.Fail("No final decision has been recorded for this tender yet.");

        await _notifier.SendDecisionNoticesAsync(tenderId, members);
        return VoteResult.Ok("Any missing decision notifications were sent. Recipients who already had one were skipped.");
    }

    // ------------------------------------------------------------------ final decision report (read-only)

    /// <summary>
    /// The final decision report, for CURRENT committee members only (re-checked in the database; having
    /// voted earlier is not enough). Returns null when the tender does not exist or no decision is recorded.
    /// </summary>
    public async Task<DecisionReportViewModel?> GetDecisionReportAsync(int tenderId, string userId)
    {
        var decision = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Decision, a.DecidedAt, a.Comment, a.SelectedBidId })
            .FirstOrDefaultAsync();
        if (decision is null) return null;

        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new { t.TenderNumber, t.Title, t.Status })
            .FirstOrDefaultAsync();
        if (tender is null) return null;

        var members = await GetEligibleMemberIdsAsync();
        var report = new DecisionReportViewModel { TenderId = tenderId };
        if (!members.Contains(userId)) return report; // CanView stays false: former members cannot see votes

        var bid = decision.SelectedBidId is int bidId ? await _db.Bids.AsNoTracking()
            .Where(b => b.Id == bidId)
            .Select(b => new { b.TotalPrice, b.Currency, Company = b.SupplierProfile.CompanyName })
            .FirstOrDefaultAsync() : null;

        var votes = await _db.TopCommitteeVotes.AsNoTracking()
            .Where(v => v.TenderId == tenderId)
            .OrderBy(v => v.VotedAt)
            .Select(v => new DecisionReportVote
            {
                MemberName = v.MemberUser.FullName,
                Vote = v.Vote,
                Reason = v.Comment,
                VotedAt = v.VotedAt
            })
            .ToListAsync();

        report.CanView = true;
        report.TenderNumber = tender.TenderNumber;
        report.TenderTitle = tender.Title;
        report.TenderStatus = tender.Status;
        report.Outcome = decision.Decision;
        report.DecidedAt = decision.DecidedAt;
        report.Summary = decision.Comment;
        report.RecommendedSupplier = bid?.Company;
        report.RecommendedPrice = bid?.TotalPrice;
        report.RecommendedCurrency = bid?.Currency;
        report.Votes = votes;
        return report;
    }

    // ------------------------------------------------------------------ helpers

    // Locks this tender's row until the transaction ends: concurrent votes/decisions on it wait here.
    private Task LockTenderAsync(int tenderId) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Tenders WITH (UPDLOCK, HOLDLOCK) WHERE Id = {tenderId}");

    // A failed commit is uncertain (the database may or may not have saved it), so the caller tells the
    // member to refresh. The duplicate-vote and "already decided" checks make a second attempt safe.
    private async Task<bool> TryCommitAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsDatabaseError(Exception ex) => ex is DbUpdateException || ex is SqlException;

    /// <summary>
    /// The tender-level rules (shared by the status page, voting and the final decision).
    /// BlockReason is null when the tender is ready for committee voting / decision.
    /// </summary>
    private async Task<TenderCheck> CheckTenderAsync(int tenderId)
    {
        var result = new TenderCheck();

        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new { t.Status, t.RequiresTopCommittee })
            .FirstOrDefaultAsync();
        if (tender is null) return result.Block("This tender does not exist.");

        // The latest Manager decision is the forward to the committee.
        var managerDecision = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.Manager)
            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.Decision, a.ReportId })
            .FirstOrDefaultAsync();

        var report = managerDecision is null ? null : await _db.EvaluationReports.AsNoTracking()
            .Where(r => r.Id == managerDecision.ReportId && r.TenderId == tenderId)
            .Select(r => new { r.Id, r.Status, r.RecommendedBidId })
            .FirstOrDefaultAsync();

        var bid = report?.RecommendedBidId is int bidId ? await _db.Bids.AsNoTracking()
            .Where(b => b.Id == bidId && b.TenderId == tenderId)
            .Select(b => new { b.Id, b.TotalPrice, b.Currency, Supplier = b.SupplierProfile.CompanyName })
            .FirstOrDefaultAsync() : null;

        if (report is not null) result.ReportId = report.Id;
        if (bid is not null)
        {
            result.RecommendedBidId = bid.Id;
            result.RecommendedSupplier = bid.Supplier;
            result.RecommendedPrice = bid.TotalPrice;
            result.RecommendedCurrency = bid.Currency;
        }

        if (tender.Status != Statuses.TenderStatus.AwaitingCommitteeDecision || !tender.RequiresTopCommittee)
            return result.Block("This tender is not awaiting a Top Committee decision.");
        if (managerDecision is null || managerDecision.Decision != Statuses.ApprovalDecision.Approved)
            return result.Block("The Sourcing Manager has not forwarded this tender to the Top Committee.");
        if (report is null || report.Status != Statuses.ReportStatus.ManagerApproved)
            return result.Block("The evaluation report for this tender is not approved by the Sourcing Manager.");
        if (bid is null)
            return result.Block("The evaluation report does not name a valid recommended bid for this tender.");

        var alreadyDecided = await _db.TenderApprovals.AsNoTracking()
            .AnyAsync(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee);
        if (alreadyDecided)
            return result.Block("The Top Committee decision for this tender has already been recorded.");

        // An unresolved clarification sent by a Top Committee account, or by anyone who already voted on
        // this tender (covers a member whose role was removed later), pauses voting and the decision.
        var committeeAccountIds = await (from userRole in _db.UserRoles
                                         join role in _db.Roles on userRole.RoleId equals role.Id
                                         where role.Name == Roles.TopCommittee
                                         select userRole.UserId).ToListAsync();
        var voterIds = await _db.TopCommitteeVotes.AsNoTracking()
            .Where(v => v.TenderId == tenderId)
            .Select(v => v.MemberUserId).ToListAsync();
        var senderIds = committeeAccountIds.Concat(voterIds).Distinct().ToList();

        var openClarification = await _db.Clarifications.AsNoTracking()
            .AnyAsync(c => c.TenderId == tenderId
                           && c.Status != Statuses.ClarificationStatus.Resolved
                           && senderIds.Contains(c.SenderUserId));
        if (openClarification)
            return result.Block("Voting is paused until the committee's clarification request is resolved by the Sourcing Manager.");

        return result;
    }
}

/// <summary>The result of the tender-level checks, shared with CommitteeDecisionService.</summary>
public class TenderCheck
{
    public string? BlockReason { get; private set; }
    public int? ReportId { get; set; }
    public int? RecommendedBidId { get; set; }
    public string? RecommendedSupplier { get; set; }
    public decimal? RecommendedPrice { get; set; }
    public string? RecommendedCurrency { get; set; }

    public TenderCheck Block(string reason)
    {
        BlockReason = reason;
        return this;
    }
}

/// <summary>The answer of a vote or retry: success flag and a message for the member.</summary>
public class VoteResult
{
    public bool Success { get; private init; }
    public string Message { get; private init; } = "";

    public static VoteResult Ok(string message) => new() { Success = true, Message = message };
    public static VoteResult Fail(string message) => new() { Success = false, Message = message };
}
