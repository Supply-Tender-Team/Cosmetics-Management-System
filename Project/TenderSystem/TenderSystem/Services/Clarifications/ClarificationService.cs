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
    private static readonly string[] ReviewTenderStatuses =
    [
        Statuses.TenderStatus.UnderEvaluation,
        Statuses.TenderStatus.AwaitingManagerReview
    ];

    private static readonly string[] OfficerManagerTenderStatuses =
    [
        Statuses.TenderStatus.UnderEvaluation,
        Statuses.TenderStatus.AwaitingManagerReview,
        Statuses.TenderStatus.AwaitingCommitteeDecision
    ];

    private static readonly string[] CommitteeRoles =
    [
        Statuses.CommitteeRole.Technical,
        Statuses.CommitteeRole.Financial,
        Statuses.CommitteeRole.Department
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

    public ClarificationService(
        ApplicationDbContext db,
        NotificationService notifications,
        AuditService audit)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<List<ClarificationRouteOption>> GetCreateRoutesAsync(
        string userId,
        IReadOnlyCollection<string> currentRoles,
        CancellationToken cancellationToken)
    {
        var routes = await BuildAllowedRoutesAsync(userId, currentRoles, cancellationToken);
        return routes.Select(route => new ClarificationRouteOption(
            route.Key,
            route.ContextLabel,
            route.StageLabel,
            route.RecipientName,
            route.RecipientRole)).ToList();
    }

    public async Task<(int? Id, bool NotificationSent)> CreateAsync(
        string userId,
        IReadOnlyCollection<string> currentRoles,
        string routeKey,
        string subject,
        string description,
        CancellationToken cancellationToken)
    {
        var routes = await BuildAllowedRoutesAsync(userId, currentRoles, cancellationToken);
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

        var sent = await NotifyAsync(
            route.ReceiverUserId,
            clarification.Id,
            "New clarification request",
            $"A clarification was opened about {route.ContextLabel}.");

        return (clarification.Id, sent);
    }

    public async Task<(bool Succeeded, bool NotificationSent)> ReplyAsync(
        int id,
        string userId,
        string body,
        CancellationToken cancellationToken)
    {
        var clarification = await _db.Clarifications
            .FirstOrDefaultAsync(item => item.Id == id
                && (item.SenderUserId == userId || item.ReceiverUserId == userId), cancellationToken);

        if (clarification is null || clarification.Status == Statuses.ClarificationStatus.Resolved)
            return (false, false);

        var otherUserId = clarification.SenderUserId == userId
            ? clarification.ReceiverUserId
            : clarification.SenderUserId;
        var previousStatus = clarification.Status;
        var message = new ClarificationMessage
        {
            ClarificationId = clarification.Id,
            SenderUserId = userId,
            Body = body.Trim(),
            SentAt = DateTime.UtcNow
        };

        clarification.Status = userId == clarification.ReceiverUserId
            ? Statuses.ClarificationStatus.Answered
            : Statuses.ClarificationStatus.Pending;
        _db.ClarificationMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Replied", "ClarificationMessage", message.Id.ToString(),
            $"Reply added to clarification #{clarification.Id}.", previousStatus);

        var sent = await NotifyAsync(
            otherUserId,
            clarification.Id,
            "New clarification reply",
            $"A new reply was added to clarification: {clarification.Subject}.");

        return (true, sent);
    }

    public async Task<(bool Succeeded, bool NotificationSent)> ResolveAsync(
        int id,
        string userId,
        bool isSourcingManager,
        CancellationToken cancellationToken)
    {
        var clarification = await _db.Clarifications
            .FirstOrDefaultAsync(item => item.Id == id
                && (item.SenderUserId == userId || item.ReceiverUserId == userId), cancellationToken);
        if (clarification is null) return (false, false);
        if (clarification.Status == Statuses.ClarificationStatus.Resolved) return (true, false);

        var senderIsTopCommittee = await (from userRole in _db.UserRoles.AsNoTracking()
                                          join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                          where userRole.UserId == clarification.SenderUserId
                                              && role.Name == Roles.TopCommittee
                                          select userRole.UserId)
            .AnyAsync(cancellationToken);

        var mayResolve = senderIsTopCommittee
            ? userId == clarification.ReceiverUserId && isSourcingManager
            : userId == clarification.SenderUserId;
        if (!mayResolve) return (false, false);

        var previousStatus = clarification.Status;
        clarification.Status = Statuses.ClarificationStatus.Resolved;
        clarification.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Resolved", "Clarification", clarification.Id.ToString(),
            "Clarification marked as resolved.", previousStatus);

        var otherUserId = clarification.SenderUserId == userId
            ? clarification.ReceiverUserId
            : clarification.SenderUserId;
        var sent = await NotifyAsync(
            otherUserId,
            clarification.Id,
            "Clarification resolved",
            $"Clarification '{clarification.Subject}' was marked as resolved.");

        return (true, sent);
    }

    private async Task<List<AllowedRoute>> BuildAllowedRoutesAsync(
        string userId,
        IReadOnlyCollection<string> currentRoles,
        CancellationToken cancellationToken)
    {
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
            int? bidId = null)
        {
            if (string.IsNullOrWhiteSpace(receiverId) || receiverId == userId) return;
            var key = $"{kind}|{contextId}|{receiverId}";
            if (!seen.Add(key)) return;

            var recipient = await _db.Users.AsNoTracking()
                .Where(user => user.Id == receiverId)
                .Select(user => new { user.Id, user.FullName })
                .SingleOrDefaultAsync(cancellationToken);
            if (recipient is null) return;

            result.Add(new AllowedRoute(key, contextLabel, stageLabel, recipient.Id,
                string.IsNullOrWhiteSpace(recipient.FullName) ? receiverRole : recipient.FullName,
                Roles.LabelFor(receiverRole), requisitionId, tenderId, bidId));
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
                    && tender.Status == Statuses.TenderStatus.UnderEvaluation
                    && tender.CommitteeMembers.Any(member => member.MemberRole == Statuses.CommitteeRole.Technical
                        || member.MemberRole == Statuses.CommitteeRole.Financial))
                .Select(tender => new
                {
                    tender.Id,
                    tender.TenderNumber,
                    tender.Title,
                    Members = tender.CommitteeMembers
                        .Where(member => member.MemberRole == Statuses.CommitteeRole.Technical
                            || member.MemberRole == Statuses.CommitteeRole.Financial)
                        .Select(member => new { member.UserId, member.MemberRole })
                })
                .ToListAsync(cancellationToken);

            foreach (var tender in tenders)
            foreach (var member in tender.Members)
            {
                await AddAsync("committee-bid", tender.Id,
                    $"Tender {tender.TenderNumber} · {tender.Title}", "Evaluation in progress",
                    member.UserId, member.MemberRole, tenderId: tender.Id);
            }
        }

        if (roles.Contains(Roles.EvaluationCommittee))
        {
            var assignments = await _db.CommitteeMembers.AsNoTracking()
                .Where(member => member.UserId == userId && CommitteeRoles.Contains(member.MemberRole)
                    && (member.Tender.Status == Statuses.TenderStatus.UnderEvaluation
                        || member.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview))
                .Select(member => new
                {
                    member.TenderId,
                    member.Tender.TenderNumber,
                    member.Tender.Title,
                    member.Tender.CreatedByUserId,
                    member.Tender.Status
                })
                .ToListAsync(cancellationToken);

            foreach (var assignment in assignments)
            {
                await AddAsync("committee-bid", assignment.TenderId,
                    $"Tender {assignment.TenderNumber} · {assignment.Title}", assignment.Status,
                    assignment.CreatedByUserId, Roles.SourcingOfficer, tenderId: assignment.TenderId);
            }
        }

        if (roles.Contains(Roles.EvaluationCommittee) || roles.Contains(Roles.SourcingManager))
        {
            var memberships = await _db.CommitteeMembers.AsNoTracking()
                .Where(member => member.UserId == userId
                    && (member.Tender.Status == Statuses.TenderStatus.UnderEvaluation
                        || member.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview))
                .Select(member => new
                {
                    member.TenderId,
                    member.MemberRole,
                    member.Tender.TenderNumber,
                    member.Tender.Title,
                    member.Tender.Status,
                    PeerIds = member.Tender.CommitteeMembers
                        .Where(peer => peer.UserId != userId)
                        .Select(peer => new { peer.UserId, peer.MemberRole })
                })
                .ToListAsync(cancellationToken);

            foreach (var membership in memberships)
            foreach (var peer in membership.PeerIds)
            {
                var isAllowedPair = membership.MemberRole == Statuses.CommitteeRole.SourcingManager
                    ? CommitteeRoles.Contains(peer.MemberRole)
                    : CommitteeRoles.Contains(membership.MemberRole)
                        && peer.MemberRole == Statuses.CommitteeRole.SourcingManager;
                if (!isAllowedPair) continue;

                var recipientRole = peer.MemberRole == Statuses.CommitteeRole.SourcingManager
                    ? Roles.SourcingManager
                    : Roles.EvaluationCommittee;
                await AddAsync("committee-manager", membership.TenderId,
                    $"Tender {membership.TenderNumber} · {membership.Title}", membership.Status,
                    peer.UserId, recipientRole, tenderId: membership.TenderId);
            }
        }

        if (roles.Contains(Roles.SourcingOfficer))
        {
            var tenders = await _db.Tenders.AsNoTracking()
                .Where(tender => tender.CreatedByUserId == userId
                    && OfficerManagerTenderStatuses.Contains(tender.Status)
                    && tender.CommitteeMembers.Any(member => member.MemberRole == Statuses.CommitteeRole.SourcingManager))
                .Select(tender => new
                {
                    tender.Id,
                    tender.TenderNumber,
                    tender.Title,
                    tender.Status,
                    Managers = tender.CommitteeMembers
                        .Where(member => member.MemberRole == Statuses.CommitteeRole.SourcingManager)
                        .Select(member => member.UserId)
                })
                .ToListAsync(cancellationToken);

            foreach (var tender in tenders)
            foreach (var managerId in tender.Managers)
            {
                await AddAsync("officer-manager", tender.Id,
                    $"Tender {tender.TenderNumber} · {tender.Title}", tender.Status,
                    managerId, Roles.SourcingManager, tenderId: tender.Id);
            }
        }

        if (roles.Contains(Roles.SourcingManager))
        {
            var tenders = await _db.CommitteeMembers.AsNoTracking()
                .Where(member => member.UserId == userId
                    && member.MemberRole == Statuses.CommitteeRole.SourcingManager
                    && OfficerManagerTenderStatuses.Contains(member.Tender.Status))
                .Select(member => new
                {
                    member.TenderId,
                    member.Tender.TenderNumber,
                    member.Tender.Title,
                    member.Tender.Status,
                    member.Tender.CreatedByUserId
                })
                .ToListAsync(cancellationToken);

            foreach (var tender in tenders)
            {
                await AddAsync("officer-manager", tender.TenderId,
                    $"Tender {tender.TenderNumber} · {tender.Title}", tender.Status,
                    tender.CreatedByUserId, Roles.SourcingOfficer, tenderId: tender.TenderId);
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
                    bid.UserId, Roles.Supplier, tenderId: bid.TenderId, bidId: bid.Id);
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
                    bid.CreatedByUserId, Roles.SourcingOfficer, tenderId: bid.TenderId, bidId: bid.Id);
            }
        }

        var topCommitteeUsers = roles.Contains(Roles.SourcingManager)
            ? await UserIdsInRoleAsync(Roles.TopCommittee, cancellationToken)
            : [];
        if (roles.Contains(Roles.SourcingManager))
        {
            var tenders = await _db.CommitteeMembers.AsNoTracking()
                .Where(member => member.UserId == userId
                    && member.MemberRole == Statuses.CommitteeRole.SourcingManager
                    && member.Tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision)
                .Select(member => new { member.TenderId, member.Tender.TenderNumber, member.Tender.Title })
                .ToListAsync(cancellationToken);
            foreach (var tender in tenders)
            foreach (var topUserId in topCommitteeUsers)
            {
                await AddAsync("top-committee", tender.TenderId,
                    $"Tender {tender.TenderNumber} · {tender.Title}", "Awaiting Top Committee decision",
                    topUserId, Roles.TopCommittee, tenderId: tender.TenderId);
            }
        }

        if (roles.Contains(Roles.TopCommittee))
        {
            var tenders = await _db.Tenders.AsNoTracking()
                .Where(tender => tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                    && tender.CommitteeMembers.Any(member => member.MemberRole == Statuses.CommitteeRole.SourcingManager))
                .Select(tender => new
                {
                    tender.Id,
                    tender.TenderNumber,
                    tender.Title,
                    Managers = tender.CommitteeMembers
                        .Where(member => member.MemberRole == Statuses.CommitteeRole.SourcingManager)
                        .Select(member => member.UserId)
                })
                .ToListAsync(cancellationToken);
            foreach (var tender in tenders)
            foreach (var managerId in tender.Managers)
            {
                await AddAsync("top-committee", tender.Id,
                    $"Tender {tender.TenderNumber} · {tender.Title}", "Awaiting Top Committee decision",
                    managerId, Roles.SourcingManager, tenderId: tender.Id);
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
