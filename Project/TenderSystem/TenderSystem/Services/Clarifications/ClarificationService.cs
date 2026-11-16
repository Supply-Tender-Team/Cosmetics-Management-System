using System.Data;
using TenderSystem.Areas.Committee.Services;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Services.Clarifications;

public sealed class ClarificationService
{
    private static readonly string[] OfficerManagerTenderStatuses =
    [
        Statuses.TenderStatus.UnderEvaluation,
        Statuses.TenderStatus.AwaitingManagerReview,
        Statuses.TenderStatus.AwaitingCommitteeDecision
    ];

    private static readonly string[] EvaluationTeamSeats =
    [
        Statuses.CommitteeRole.Department,
        Statuses.CommitteeRole.Technical,
        Statuses.CommitteeRole.Financial,
        Statuses.CommitteeRole.SourcingManager   // the Department Financial Officer: an Evaluation Committee account, NOT the Sourcing Manager
    ];

    private static readonly string[] EvaluationTenderStatuses =
    [
        Statuses.TenderStatus.Published,
        Statuses.TenderStatus.Open,
        Statuses.TenderStatus.Closed,
        Statuses.TenderStatus.UnderEvaluation,
        Statuses.TenderStatus.AwaitingManagerReview,
        Statuses.TenderStatus.AwaitingCommitteeDecision
    ];

    private static readonly string[] SupplierBidStatuses =
    [
        Statuses.BidStatus.Submitted,
        Statuses.BidStatus.UnderReview,
        Statuses.BidStatus.Compliant,
        Statuses.BidStatus.NonCompliant
    ];

    private static readonly string[] SupplierTenderStatuses =
    [
        Statuses.TenderStatus.Open,
        Statuses.TenderStatus.Closed,
        Statuses.TenderStatus.UnderEvaluation
    ];

    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;
    private readonly CommitteeVotingService _committeeVoting;

    public ClarificationService(
        ApplicationDbContext db,
        NotificationService notifications,
        AuditService audit,
        CommitteeVotingService committeeVoting)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
        _committeeVoting = committeeVoting;
    }

    public async Task<List<ClarificationRouteOption>> GetCreateRoutesAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var routes = await BuildAllowedRoutesAsync(userId, cancellationToken);
        return routes.Select(route => new ClarificationRouteOption(
            route.Key,
            route.ContextLabel,
            route.StageLabel,
            route.RecipientName,
            route.RecipientRole)).ToList();
    }

    public async Task<(int? Id, bool NotificationSent)> CreateAsync(
        string userId,
        string routeKey,
        string subject,
        string description,
        CancellationToken cancellationToken)
    {
        int clarificationId;
        string receiverId;
        string contextLabel;
        await using (var transaction = await _db.Database.BeginTransactionAsync(
                         IsolationLevel.Serializable, cancellationToken))
        {
            var routes = await BuildAllowedRoutesAsync(userId, cancellationToken);
            var route = routes.FirstOrDefault(item => item.Key == routeKey);
            if (route is null) return (null, false);

            var now = DateTime.UtcNow;
            var clarification = new Clarification
            {
                Subject = subject.Trim(),
                Description = description.Trim(),
                Status = Statuses.ClarificationStatus.Pending,
                SenderUserId = userId,
                ReceiverUserId = route.ReceiverUserId,
                RequisitionId = route.RequisitionId,
                TenderId = route.TenderId,
                BidId = route.BidId,
                CreatedAt = now
            };
            clarification.ClarificationMessages.Add(new ClarificationMessage
            {
                SenderUserId = userId,
                Body = description.Trim(),
                SentAt = now
            });

            _db.Clarifications.Add(clarification);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Created", "Clarification", clarification.Id.ToString(),
                $"Clarification opened for {route.ContextLabel}.");
            await transaction.CommitAsync(cancellationToken);
            clarificationId = clarification.Id;
            receiverId = route.ReceiverUserId;
            contextLabel = route.ContextLabel;
        }

        var sent = await NotifyAsync(receiverId, clarificationId,
            "New clarification request", $"A clarification was opened about {contextLabel}.");
        return (clarificationId, sent);
    }

    public async Task<ClarificationInteractionPermissions> GetInteractionPermissionsAsync(
        int id,
        string userId,
        CancellationToken cancellationToken)
    {
        var clarification = await _db.Clarifications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id
                && (item.SenderUserId == userId || item.ReceiverUserId == userId), cancellationToken);
        if (clarification is null) return new(false, false);

        var isOpen = clarification.Status != Statuses.ClarificationStatus.Resolved;
        if (!isOpen) return new(false, false);

        var routeIsCurrent = await HasCurrentReciprocalRouteAsync(clarification, userId, cancellationToken);
        var canResolve = await CanResolveAsync(clarification, userId, routeIsCurrent, cancellationToken);
        return new(routeIsCurrent, canResolve);
    }

    public async Task<(bool Succeeded, bool NotificationSent)> ReplyAsync(
        int id,
        string userId,
        string body,
        CancellationToken cancellationToken)
    {
        var trimmedBody = body?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedBody) || trimmedBody.Length > 2000) return (false, false);

        int clarificationId;
        string otherUserId;
        string subject;
        await using (var transaction = await _db.Database.BeginTransactionAsync(
                         IsolationLevel.Serializable, cancellationToken))
        {
            await LockClarificationAsync(id, cancellationToken);
            var clarification = await _db.Clarifications
                .FirstOrDefaultAsync(item => item.Id == id
                    && (item.SenderUserId == userId || item.ReceiverUserId == userId), cancellationToken);
            if (clarification is null || clarification.Status == Statuses.ClarificationStatus.Resolved)
                return (false, false);

            if (!await HasCurrentReciprocalRouteAsync(clarification, userId, cancellationToken))
                return (false, false);

            otherUserId = clarification.SenderUserId == userId
                ? clarification.ReceiverUserId
                : clarification.SenderUserId;
            var previousStatus = clarification.Status;
            var message = new ClarificationMessage
            {
                ClarificationId = clarification.Id,
                SenderUserId = userId,
                Body = trimmedBody,
                SentAt = DateTime.UtcNow
            };
            clarification.Status = userId == clarification.ReceiverUserId
                ? Statuses.ClarificationStatus.Answered
                : Statuses.ClarificationStatus.Pending;
            _db.ClarificationMessages.Add(message);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Replied", "ClarificationMessage", message.Id.ToString(),
                $"Reply added to clarification #{clarification.Id}.", previousStatus);
            await transaction.CommitAsync(cancellationToken);
            clarificationId = clarification.Id;
            subject = clarification.Subject;
        }

        var sent = await NotifyAsync(otherUserId, clarificationId,
            "New clarification reply", $"A new reply was added to clarification: {subject}.");
        return (true, sent);
    }

    public async Task<(bool Succeeded, bool NotificationSent)> ResolveAsync(
        int id,
        string userId,
        CancellationToken cancellationToken)
    {
        int clarificationId;
        string otherUserId;
        string subject;
        await using (var transaction = await _db.Database.BeginTransactionAsync(
                         IsolationLevel.Serializable, cancellationToken))
        {
            await LockClarificationAsync(id, cancellationToken);
            var clarification = await _db.Clarifications
                .FirstOrDefaultAsync(item => item.Id == id
                    && (item.SenderUserId == userId || item.ReceiverUserId == userId), cancellationToken);
            if (clarification is null) return (false, false);
            if (clarification.Status == Statuses.ClarificationStatus.Resolved) return (true, false);

            var routeIsCurrent = await HasCurrentReciprocalRouteAsync(clarification, userId, cancellationToken);
            if (!await CanResolveAsync(clarification, userId, routeIsCurrent, cancellationToken))
                return (false, false);

            otherUserId = clarification.SenderUserId == userId
                ? clarification.ReceiverUserId
                : clarification.SenderUserId;
            var previousStatus = clarification.Status;
            clarification.Status = Statuses.ClarificationStatus.Resolved;
            clarification.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Resolved", "Clarification", clarification.Id.ToString(),
                "Clarification marked as resolved.", previousStatus);
            await transaction.CommitAsync(cancellationToken);
            clarificationId = clarification.Id;
            subject = clarification.Subject;
        }

        var sent = await NotifyAsync(otherUserId, clarificationId,
            "Clarification resolved", $"Clarification '{subject}' was marked as resolved.");
        return (true, sent);
    }

    private async Task<List<AllowedRoute>> BuildAllowedRoutesAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var currentRoles = await (from userRole in _db.UserRoles.AsNoTracking()
                                  join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                  where userRole.UserId == userId
                                  select role.Name!).ToListAsync(cancellationToken);
        var roles = currentRoles.ToHashSet(StringComparer.Ordinal);
        var result = new List<AllowedRoute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        async Task AddAsync(
            string kind,
            int contextId,
            string contextLabel,
            string stageLabel,
            string receiverId,
            string receiverRole,
            int? requisitionId = null,
            int? tenderId = null,
            int? bidId = null,
            string? recipientRoleLabel = null)
        {
            if (string.IsNullOrWhiteSpace(receiverId) || receiverId == userId) return;
            var key = $"{kind}|{contextId}|{receiverId}";
            if (!seen.Add(key)) return;

            var recipient = await _db.Users.AsNoTracking()
                .Where(user => user.Id == receiverId && user.IsActive)
                .Select(user => new { user.Id, user.FullName })
                .SingleOrDefaultAsync(cancellationToken);
            if (recipient is null) return;

            result.Add(new AllowedRoute(key, contextLabel, stageLabel, recipient.Id,
                string.IsNullOrWhiteSpace(recipient.FullName) ? receiverRole : recipient.FullName,
                recipientRoleLabel ?? Roles.LabelFor(receiverRole), requisitionId, tenderId, bidId));
        }

        var sourcingOfficerIds = roles.Contains(Roles.Requester)
            ? await UserIdsInRoleAsync(Roles.SourcingOfficer, cancellationToken)
            : [];

        if (roles.Contains(Roles.Requester))
        {
            var requests = await _db.PurchaseRequisitions.AsNoTracking()
                .Where(request => request.RequestedByUserId == userId
                    && request.Status != Statuses.RequestStatus.Draft
                    && request.Status != Statuses.RequestStatus.Rejected
                    && request.Status != Statuses.RequestStatus.ConvertedToTender)
                .Select(request => new { request.Id, request.RequestNumber, request.Title, request.Status, request.ReviewedByUserId })
                .ToListAsync(cancellationToken);

            foreach (var request in requests)
            {
                var receivers = !string.IsNullOrWhiteSpace(request.ReviewedByUserId)
                    && sourcingOfficerIds.Contains(request.ReviewedByUserId)
                    ? [request.ReviewedByUserId]
                    : sourcingOfficerIds;
                foreach (var receiverId in receivers)
                {
                    await AddAsync("request", request.Id,
                        $"Request {request.RequestNumber} · {request.Title}", request.Status,
                        receiverId, Roles.SourcingOfficer, requisitionId: request.Id);
                }
            }
        }

        if (roles.Contains(Roles.SourcingOfficer))
        {
            var requests = await _db.PurchaseRequisitions.AsNoTracking()
                .Where(request => request.Status == Statuses.RequestStatus.Submitted
                    || request.Status == Statuses.RequestStatus.UnderReview
                    || request.Status == Statuses.RequestStatus.Returned)
                .Where(request => request.ReviewedByUserId == null || request.ReviewedByUserId == userId)
                .Select(request => new { request.Id, request.RequestNumber, request.Title, request.Status, request.RequestedByUserId })
                .ToListAsync(cancellationToken);

            foreach (var request in requests)
            {
                await AddAsync("request", request.Id,
                    $"Request {request.RequestNumber} · {request.Title}", request.Status,
                    request.RequestedByUserId, Roles.Requester, requisitionId: request.Id);
            }
        }

        if (roles.Contains(Roles.SourcingOfficer))
        {
            var tenders = await _db.Tenders.AsNoTracking()
                .Where(tender => tender.CreatedByUserId == userId
                    && EvaluationTenderStatuses.Contains(tender.Status))
                .Select(tender => new
                {
                    tender.Id,
                    tender.TenderNumber,
                    tender.Title,
                    tender.Status,
                    Members = tender.CommitteeMembers
                        .Where(member => EvaluationTeamSeats.Contains(member.MemberRole)
                            && _db.Notifications.Any(notification => notification.UserId == member.UserId
                                && notification.Type == "TenderEvaluation"
                                && notification.Link == "/Sourcing/CommitteeBrief/Index/" + tender.Id))
                        .Select(member => new { member.UserId, member.MemberRole })
                })
                .ToListAsync(cancellationToken);

            foreach (var tender in tenders)
            foreach (var member in tender.Members)
            {
                await AddAsync("officer-evaluator", tender.Id,
                    $"Tender {tender.TenderNumber} · {tender.Title}", tender.Status,
                    member.UserId, member.MemberRole, tenderId: tender.Id,
                    recipientRoleLabel: "Evaluation team member");
            }
        }

        var evaluatorAssignments = await _db.CommitteeMembers.AsNoTracking()
            .Where(member => member.UserId == userId
                && EvaluationTeamSeats.Contains(member.MemberRole)
                && EvaluationTenderStatuses.Contains(member.Tender.Status)
                && _db.Notifications.Any(notification => notification.UserId == userId
                    && notification.Type == "TenderEvaluation"
                    && notification.Link == "/Sourcing/CommitteeBrief/Index/" + member.TenderId))
            .Select(member => new
            {
                member.TenderId,
                member.Tender.TenderNumber,
                member.Tender.Title,
                member.Tender.Status,
                member.Tender.CreatedByUserId,
                OtherEvaluators = member.Tender.CommitteeMembers
                    .Where(other => other.UserId != userId
                        && EvaluationTeamSeats.Contains(other.MemberRole)
                        && _db.Notifications.Any(notification => notification.UserId == other.UserId
                            && notification.Type == "TenderEvaluation"
                            && notification.Link == "/Sourcing/CommitteeBrief/Index/" + member.TenderId))
                    .Select(other => new { other.UserId, other.MemberRole })
            })
            .ToListAsync(cancellationToken);

        foreach (var assignment in evaluatorAssignments)
        {
            var contextLabel = $"Tender {assignment.TenderNumber} · {assignment.Title}";
            await AddAsync("officer-evaluator", assignment.TenderId, contextLabel, assignment.Status,
                assignment.CreatedByUserId, Roles.SourcingOfficer, tenderId: assignment.TenderId,
                recipientRoleLabel: "Sourcing Officer");

            foreach (var otherEvaluator in assignment.OtherEvaluators)
            {
                await AddAsync("evaluation-team", assignment.TenderId, contextLabel, assignment.Status,
                    otherEvaluator.UserId, otherEvaluator.MemberRole, tenderId: assignment.TenderId,
                    recipientRoleLabel: "Evaluation team member");
            }
        }

        // Evaluators do not message the Sourcing Manager or Top Committee directly.

        if (roles.Contains(Roles.SourcingOfficer))
        {
            var tenders = await _db.Tenders.AsNoTracking()
                .Where(tender => tender.CreatedByUserId == userId
                    && OfficerManagerTenderStatuses.Contains(tender.Status))
                .Select(tender => new { tender.Id, tender.TenderNumber, tender.Title, tender.Status })
                .ToListAsync(cancellationToken);
            var managerIds = tenders.Count == 0
                ? []
                : await UserIdsInRoleAsync(Roles.SourcingManager, cancellationToken);

            foreach (var tender in tenders)
            foreach (var managerId in managerIds)
            {
                await AddAsync("officer-manager", tender.Id,
                    $"Tender {tender.TenderNumber} · {tender.Title}", tender.Status,
                    managerId, Roles.SourcingManager, tenderId: tender.Id,
                    recipientRoleLabel: "Sourcing Manager");
            }
        }

        if (roles.Contains(Roles.SourcingManager))
        {
            var tenders = await _db.Tenders.AsNoTracking()
                .Where(tender => OfficerManagerTenderStatuses.Contains(tender.Status))
                .Select(tender => new
                {
                    TenderId = tender.Id,
                    tender.TenderNumber,
                    tender.Title,
                    tender.Status,
                    tender.CreatedByUserId
                })
                .ToListAsync(cancellationToken);

            foreach (var tender in tenders)
            {
                await AddAsync("officer-manager", tender.TenderId,
                    $"Tender {tender.TenderNumber} · {tender.Title}", tender.Status,
                    tender.CreatedByUserId, Roles.SourcingOfficer, tenderId: tender.TenderId,
                    recipientRoleLabel: "Sourcing Officer");
            }
        }

        if (roles.Contains(Roles.SourcingOfficer))
        {
            var bids = await _db.Bids.AsNoTracking()
                .Where(bid => bid.Tender.CreatedByUserId == userId
                    && SupplierBidStatuses.Contains(bid.Status)
                    && SupplierTenderStatuses.Contains(bid.Tender.Status))
                .Select(bid => new
                {
                    bid.Id,
                    bid.TenderId,
                    bid.Tender.TenderNumber,
                    bid.SupplierProfile.CompanyName,
                    bid.SupplierProfile.UserId,
                    bid.Tender.Status
                })
                .ToListAsync(cancellationToken);

            foreach (var bid in bids)
            {
                await AddAsync("supplier-bid", bid.Id,
                    $"Tender {bid.TenderNumber} · Bid #{bid.Id} · {bid.CompanyName}", bid.Status,
                    bid.UserId, Roles.Supplier, tenderId: bid.TenderId, bidId: bid.Id,
                    recipientRoleLabel: "Supplier");
            }
        }

        if (roles.Contains(Roles.Supplier))
        {
            var bids = await _db.Bids.AsNoTracking()
                .Where(bid => bid.SupplierProfile.UserId == userId
                    && SupplierBidStatuses.Contains(bid.Status)
                    && SupplierTenderStatuses.Contains(bid.Tender.Status))
                .Select(bid => new
                {
                    bid.Id,
                    bid.TenderId,
                    bid.Tender.TenderNumber,
                    bid.SupplierProfile.CompanyName,
                    bid.Tender.CreatedByUserId,
                    bid.Tender.Status
                })
                .ToListAsync(cancellationToken);

            foreach (var bid in bids)
            {
                await AddAsync("supplier-bid", bid.Id,
                    $"Tender {bid.TenderNumber} · Bid #{bid.Id} · {bid.CompanyName}", bid.Status,
                    bid.CreatedByUserId, Roles.SourcingOfficer, tenderId: bid.TenderId, bidId: bid.Id,
                    recipientRoleLabel: "Sourcing Officer");
            }
        }

        var eligibleTopMembers = roles.Contains(Roles.SourcingManager) || roles.Contains(Roles.TopCommittee)
            ? await _committeeVoting.GetEligibleMemberIdsAsync()
            : [];
        if (roles.Contains(Roles.SourcingManager))
        {
            foreach (var topUserId in eligibleTopMembers)
            {
                var votingList = await _committeeVoting.GetVotingListAsync(topUserId);
                foreach (var tender in votingList?.Awaiting ?? [])
                {
                    var managerAssigned = (await ManagerIdsOfTenderAsync(tender.TenderId, cancellationToken)).Contains(userId);
                    if (!managerAssigned) continue;
                    await AddAsync("top-committee", tender.TenderId,
                        $"Tender {tender.TenderNumber} · {tender.Title}", "Awaiting Top Committee decision",
                        topUserId, Roles.TopCommittee, tenderId: tender.TenderId,
                        recipientRoleLabel: "Top Committee member");
                }
            }
        }

        if (roles.Contains(Roles.TopCommittee))
        {
            if (eligibleTopMembers.Contains(userId))
            {
                var votingList = await _committeeVoting.GetVotingListAsync(userId);
                foreach (var tender in votingList?.Awaiting ?? [])
                {
                    var managerIds = await ManagerIdsOfTenderAsync(tender.TenderId, cancellationToken);
                    foreach (var managerId in managerIds)
                        await AddAsync("top-committee", tender.TenderId,
                            $"Tender {tender.TenderNumber} · {tender.Title}", "Awaiting Top Committee decision",
                            managerId, Roles.SourcingManager, tenderId: tender.TenderId,
                            recipientRoleLabel: "Sourcing Manager");
                }
            }
        }

        return result.OrderBy(item => item.ContextLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RecipientRole, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<string>> UserIdsInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        await (from userRole in _db.UserRoles.AsNoTracking()
               join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
               where role.Name == roleName
               select userRole.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private async Task<bool> HasCurrentReciprocalRouteAsync(
        Clarification clarification,
        string userId,
        CancellationToken cancellationToken)
    {
        var otherUserId = clarification.SenderUserId == userId
            ? clarification.ReceiverUserId
            : clarification.SenderUserId;
        var routes = await BuildAllowedRoutesAsync(userId, cancellationToken);
        return routes.Any(route => route.ReceiverUserId == otherUserId
            && route.RequisitionId == clarification.RequisitionId
            && route.TenderId == clarification.TenderId
            && route.BidId == clarification.BidId);
    }

    private async Task<bool> CanResolveAsync(
        Clarification clarification,
        string userId,
        bool routeIsCurrent,
        CancellationToken cancellationToken)
    {
        if (clarification.SenderUserId == userId) return routeIsCurrent;

        // A manager may close a Top Committee clarification only for an assigned tender.
        // Keep this exception for prior voters even if their role was later removed: the
        // existing voting workflow uses unresolved voter threads to pause voting.
        if (clarification.ReceiverUserId != userId || clarification.TenderId is not int tenderId)
            return false;
        var senderWasTopCommitteeAccount = await (from userRole in _db.UserRoles.AsNoTracking()
                                                   join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                                   where userRole.UserId == clarification.SenderUserId
                                                       && role.Name == Roles.TopCommittee
                                                   select userRole.UserId).AnyAsync(cancellationToken);
        var senderHasVote = await _db.TopCommitteeVotes.AsNoTracking()
            .AnyAsync(vote => vote.TenderId == tenderId && vote.MemberUserId == clarification.SenderUserId,
                cancellationToken);
        if (!senderWasTopCommitteeAccount && !senderHasVote) return false;

        return (await ManagerIdsOfTenderAsync(tenderId, cancellationToken)).Contains(userId);
    }

    /// <summary>
    /// The Sourcing Manager(s) of a tender. The Sourcing Manager is not a committee member: it is the manager account(s) that
    /// forwarded the tender to the Top Committee, or every Sourcing Manager account when nobody has decided it yet.
    /// </summary>
    private async Task<List<string>> ManagerIdsOfTenderAsync(int tenderId, CancellationToken cancellationToken)
    {
        var managers = await UserIdsInRoleAsync(Roles.SourcingManager, cancellationToken);
        var forwarded = await _db.TenderApprovals.AsNoTracking()
            .Where(approval => approval.TenderId == tenderId
                && approval.Level == Statuses.ApprovalLevel.Manager
                && approval.Decision == Statuses.ApprovalDecision.Approved)
            .Select(approval => approval.DecidedByUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var forwardedManagers = forwarded.Where(managers.Contains).ToList();
        return forwardedManagers.Count > 0 ? forwardedManagers : managers;
    }

    private async Task LockClarificationAsync(int id, CancellationToken cancellationToken)
    {
        // Serialize replies/resolution for one thread before checking its current state.
        await _db.Clarifications.FromSqlInterpolated(
                $"SELECT * FROM [Clarifications] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().Select(item => item.Id).AnyAsync(cancellationToken);
    }

    private async Task<bool> NotifyAsync(string receiverId, int clarificationId, string title, string message)
    {
        try
        {
            await _notifications.NotifyAsync(receiverId, "ClarificationMessage", title, message,
                $"/Shared/Clarifications/Details/{clarificationId}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record AllowedRoute(
        string Key,
        string ContextLabel,
        string StageLabel,
        string ReceiverUserId,
        string RecipientName,
        string RecipientRole,
        int? RequisitionId,
        int? TenderId,
        int? BidId);
}

public sealed record ClarificationInteractionPermissions(bool CanReply, bool CanResolve);
