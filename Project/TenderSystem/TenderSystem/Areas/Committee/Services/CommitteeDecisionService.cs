using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (helping Dana with TOP-02) - Phase 2: the final Top Committee decision.
//
// IMPORTANT: TryFinalizeAsync must be called INSIDE a transaction that already holds the tender-row lock
// (CommitteeVotingService does this), so the fourth vote and the decision are saved together or not at all.
//
// What a decision changes (approved rules):
//  - Award (4 of 4 Approve): Tender.Status = Awarded, Tender.AwardedSupplierId, one TopCommittee TenderApproval.
//    The recommended offer and supplier must still be eligible (checked only for an award).
//  - Rejection (any Reject): Tender.Status = Rejected, one TopCommittee TenderApproval.
//    Supplier eligibility is NOT required for a rejection.
//  - Bid and EvaluationReport statuses are NOT changed. No new tender is created.
//
// Confidentiality: until the decision is saved, the outcome is never stated. When an award is blocked
// (offer or supplier no longer eligible), the message and the audit entry are a neutral text without the cause.
// Note: a rejection is never blocked by supplier eligibility (approved rule), so a "decision on hold" after four
// votes can only happen for an award; the text does not say so, but it can be inferred. See the final report.
public class CommitteeDecisionService
{
    // The tender status for a rejection (approved addition to Statuses.TenderStatus).
    private const string RejectedTenderStatus = Statuses.TenderStatus.Rejected;

    // Offers that can still be awarded (not Draft, Withdrawn, Non-Compliant, Accepted or Rejected).
    private static readonly string[] AwardableBidStatuses =
        [Statuses.BidStatus.Submitted, Statuses.BidStatus.UnderReview, Statuses.BidStatus.Compliant];

    /// <summary>The same text for every outcome-dependent block, so it never reveals the outcome.</summary>
    public const string NeutralBlockedMessage =
        "All four votes have been received, but the final decision cannot be recorded yet. The votes are kept. " +
        "Ask the Sourcing Manager to review the recommended offer and supplier, then retry after the issue is resolved.";

    private const string NeutralBlockedAudit = "Final decision could not be recorded yet; the four votes are kept.";
    private const string BlockedAuditAction = "TopCommitteeDecisionBlocked";

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;

    public CommitteeDecisionService(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Can a final decision be recorded now, and what would it be?
    /// Expects the tender-level checks (status, forward, report, clarifications) to have passed already.
    /// lockAwardRows = true (only inside the decision transaction) locks the recommended bid and the
    /// supplier row before reading them, so an Admin suspension or a withdrawal cannot slip in between
    /// the eligibility check and the award. The status page calls it read-only (false).
    /// </summary>
    public async Task<DecisionPlan> EvaluateAsync(int tenderId, List<string> members, TenderCheck check, bool lockAwardRows = false)
    {
        var votes = await _db.TopCommitteeVotes.AsNoTracking()
            .Where(v => v.TenderId == tenderId && members.Contains(v.MemberUserId))
            .Select(v => new { v.MemberUserId, v.Vote, v.SelectedBidId })
            .ToListAsync();

        if (members.Count != CommitteeVotingService.RequiredMembers
            || votes.Count != CommitteeVotingService.RequiredMembers
            || votes.Select(v => v.MemberUserId).Distinct().Count() != CommitteeVotingService.RequiredMembers)
            return DecisionPlan.NotReady();

        if (check.RecommendedBidId is not int recommendedBidId || check.ReportId is not int)
            return DecisionPlan.Blocked("The evaluation report does not name a valid recommended bid.");

        // Every vote must be about the bid the report recommends now (this does not depend on the outcome).
        if (votes.Any(v => v.SelectedBidId != recommendedBidId))
            return DecisionPlan.Blocked("The recommended offer changed after voting started. The Sourcing Manager must review this tender before a decision can be recorded.");

        var approvals = votes.Count(v => v.Vote == Statuses.VoteType.Approve);
        var rejections = votes.Count(v => v.Vote == Statuses.VoteType.Reject);
        var retenders = votes.Count(v => v.Vote == Statuses.VoteType.Retender);
        if (approvals + rejections + retenders != CommitteeVotingService.RequiredMembers)
            return DecisionPlan.Blocked("An unrecognized committee vote must be reviewed before the decision can be recorded.");

        // Any Reject: reject the tender. Supplier eligibility does not matter for a rejection.
        if (rejections > 0)
            return DecisionPlan.Ready(Statuses.ApprovalDecision.Rejected, approvals, rejections, retenders, null);

        // Retender closes this voting round without testing award eligibility or creating a new tender.
        if (retenders > 0)
            return DecisionPlan.Ready(Statuses.ApprovalDecision.RetenderRequired, approvals, rejections, retenders, null);

        // 4 of 4 Approve: the recommended offer and supplier must still be eligible.
        if (lockAwardRows)
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM Bids WITH (UPDLOCK, HOLDLOCK) WHERE Id = {recommendedBidId}");

        var bid = await _db.Bids.AsNoTracking()
            .Where(b => b.Id == recommendedBidId && b.TenderId == tenderId)
            .Select(b => new { b.Status, b.SupplierProfileId })
            .FirstOrDefaultAsync();
        if (bid is null || !AwardableBidStatuses.Contains(bid.Status))
            return DecisionPlan.Blocked(NeutralBlockedMessage, neutral: true);

        // Admin's supplier suspension updates this same SupplierProfiles row (and then the user) in one
        // transaction, so with this lock the two workflows run one after the other.
        if (lockAwardRows)
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM SupplierProfiles WITH (UPDLOCK, HOLDLOCK) WHERE Id = {bid.SupplierProfileId}");

        var supplier = await _db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Id == bid.SupplierProfileId)
            .Select(s => new { s.Status, Active = s.User.IsActive })
            .FirstOrDefaultAsync();
        if (supplier is null || supplier.Status != Statuses.SupplierStatus.Approved || !supplier.Active)
            return DecisionPlan.Blocked(NeutralBlockedMessage, neutral: true);

        return DecisionPlan.Ready(Statuses.ApprovalDecision.Approved, approvals, rejections, retenders, bid.SupplierProfileId);
    }

    /// <summary>
    /// Records the final decision if it can be made now. Call only inside the locked transaction.
    /// Returns NotReady / Blocked (nothing changed except, at most, one audit entry) or Finalized.
    /// </summary>
    public async Task<DecisionPlan> TryFinalizeAsync(int tenderId, string finalizingUserId, List<string> members, TenderCheck check)
    {
        var plan = await EvaluateAsync(tenderId, members, check, lockAwardRows: true);
        if (plan.Kind == DecisionKind.NotReady) return plan;

        if (plan.Kind == DecisionKind.Blocked)
        {
            await AuditBlockedOnceAsync(tenderId, plan.AuditText);
            return plan;
        }

        // Change the tender only if it is still awaiting the committee (and, for an award, not awarded yet).
        int changedTenders;
        if (plan.Outcome == Statuses.ApprovalDecision.Approved)
        {
            var supplierId = plan.AwardedSupplierProfileId!.Value;
            changedTenders = await _db.Tenders
                .Where(t => t.Id == tenderId
                            && t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                            && t.AwardedSupplierId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, Statuses.TenderStatus.Awarded)
                    .SetProperty(t => t.AwardedSupplierId, supplierId));
        }
        else if (plan.Outcome == Statuses.ApprovalDecision.Rejected)
        {
            // Rejection: only the tender status changes. Bids, the evaluation report and all history are kept.
            changedTenders = await _db.Tenders
                .Where(t => t.Id == tenderId && t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, RejectedTenderStatus));
        }
        else
        {
            changedTenders = await _db.Tenders
                .Where(t => t.Id == tenderId && t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                            && t.AwardedSupplierId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, Statuses.TenderStatus.RetenderRequired));
        }

        if (changedTenders != 1)
        {
            var blocked = DecisionPlan.Blocked("The tender changed while the decision was being recorded. No decision was saved; please refresh and try again.");
            await AuditBlockedOnceAsync(tenderId, blocked.AuditText);
            return blocked;
        }

        var decision = new TenderApproval
        {
            TenderId = tenderId,
            ReportId = check.ReportId!.Value,
            DecidedByUserId = finalizingUserId, // the member whose action completed the decision
            Level = Statuses.ApprovalLevel.TopCommittee,
            Decision = plan.Outcome!,
            SelectedBidId = check.RecommendedBidId,
            Comment = plan.Outcome == Statuses.ApprovalDecision.Approved
                ? "All 4 Top Committee members approved the recommended supplier."
                : plan.Outcome == Statuses.ApprovalDecision.Rejected
                    ? $"Rejected by the Top Committee: {plan.Rejections} of 4 members rejected."
                    : $"Retender required by the Top Committee: {plan.Retenders} of 4 members requested a retender.",
            DecidedAt = DateTime.UtcNow
        };
        _db.TenderApprovals.Add(decision);
        await _db.SaveChangesAsync();

        // Audit: the outcome and the tally only (the decision is saved now). Votes and reasons stay private.
        await _audit.LogAsync("TopCommitteeDecision", "Tender", tenderId.ToString(),
            plan.Outcome == Statuses.ApprovalDecision.RetenderRequired
                ? $"Decision #{decision.Id}: {plan.Outcome} ({plan.Approvals} approve, {plan.Retenders} retender)."
                : $"Decision #{decision.Id}: {plan.Outcome} ({plan.Approvals} approve, {plan.Rejections} reject).");
        if (plan.Outcome == Statuses.ApprovalDecision.Approved)
            await _audit.LogAsync("TenderAwarded", "Tender", tenderId.ToString(),
                $"Awarded to supplier profile #{plan.AwardedSupplierProfileId}.",
                oldValues: $"Status: {Statuses.TenderStatus.AwaitingCommitteeDecision}");
        else if (plan.Outcome == Statuses.ApprovalDecision.Rejected)
            await _audit.LogAsync("TenderRejected", "Tender", tenderId.ToString(),
                "Rejected by the Top Committee.",
                oldValues: $"Status: {Statuses.TenderStatus.AwaitingCommitteeDecision}");
        else
            await _audit.LogAsync("TenderRetenderRequired", "Tender", tenderId.ToString(),
                "Retender required by the Top Committee; no successor tender was created.",
                oldValues: $"Status: {Statuses.TenderStatus.AwaitingCommitteeDecision}");

        plan.MarkFinalized(decision.Id);
        return plan;
    }

    // Writes a "decision blocked" audit entry unless the latest one for this tender has the same text,
    // so repeated retries do not fill the audit log. Runs under the tender lock, so it is consistent.
    private async Task AuditBlockedOnceAsync(int tenderId, string text)
    {
        var entityId = tenderId.ToString();
        var lastBlockedText = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Tender" && a.EntityId == entityId && a.Action == BlockedAuditAction)
            .OrderByDescending(a => a.Id)
            .Select(a => a.NewValues)
            .FirstOrDefaultAsync();
        if (lastBlockedText != text)
            await _audit.LogAsync(BlockedAuditAction, "Tender", entityId, text);
    }

    /// <summary>The neutral text used in the audit log for outcome-dependent blocks.</summary>
    internal static string AuditTextFor(string message, bool neutral) => neutral ? NeutralBlockedAudit : message;
}

public enum DecisionKind { NotReady, Blocked, Ready, Finalized }

/// <summary>The result of evaluating / recording the final decision.</summary>
public class DecisionPlan
{
    public DecisionKind Kind { get; private set; }
    public string Message { get; private set; } = "";
    public string AuditText { get; private init; } = "";
    public string? Outcome { get; private init; }            // Approved, Rejected or Retender Required
    public int Approvals { get; private init; }
    public int Rejections { get; private init; }
    public int Retenders { get; private init; }
    public int? AwardedSupplierProfileId { get; private init; }
    public int? DecisionId { get; private set; }

    public static DecisionPlan NotReady() =>
        new() { Kind = DecisionKind.NotReady, Message = "Waiting for all four votes." };

    /// <summary>neutral = true for outcome-dependent reasons: message and audit never reveal the outcome.</summary>
    public static DecisionPlan Blocked(string reason, bool neutral = false) =>
        new()
        {
            Kind = DecisionKind.Blocked,
            Message = reason,
            AuditText = CommitteeDecisionService.AuditTextFor(reason, neutral)
        };

    public static DecisionPlan Ready(string outcome, int approvals, int rejections, int retenders, int? supplierProfileId) =>
        new()
        {
            Kind = DecisionKind.Ready,
            Outcome = outcome,
            Approvals = approvals,
            Rejections = rejections,
            Retenders = retenders,
            AwardedSupplierProfileId = supplierProfileId,
            Message = "The final decision can be recorded."
        };

    public void MarkFinalized(int decisionId)
    {
        Kind = DecisionKind.Finalized;
        DecisionId = decisionId;
        Message = Outcome == Statuses.ApprovalDecision.Approved
            ? "Final decision recorded: the tender was awarded to the recommended supplier."
            : Outcome == Statuses.ApprovalDecision.Rejected
                ? "Final decision recorded: the tender was rejected."
                : "Final decision recorded: retender required. The Sourcing Officer must create any successor tender manually.";
    }
}
