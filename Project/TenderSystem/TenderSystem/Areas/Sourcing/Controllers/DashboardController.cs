using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class DashboardController : Controller
{
    private const int PreviewLimit = 5;
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var approvedRequests = _db.PurchaseRequisitions.AsNoTracking()
            .Where(request => request.Status == Statuses.RequestStatus.Approved);
        var recentRequests = _db.PurchaseRequisitions.AsNoTracking().Where(request => request.Status != "Draft");
        var activeTenders = _db.Tenders.AsNoTracking()
            .Where(tender => tender.Status == Statuses.TenderStatus.Published
                || tender.Status == Statuses.TenderStatus.Open);
        var incomingOffers = _db.Bids.AsNoTracking()
            .Where(bid => bid.SubmittedAt.HasValue && (bid.Status == Statuses.BidStatus.Submitted
                || bid.Status == Statuses.BidStatus.UnderReview));

        // The calendar shows today and the next six days in Amman.
        var utcNow = DateTime.UtcNow;
        var calendarZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
        var today = TimeZoneInfo.ConvertTimeFromUtc(utcNow, calendarZone).Date;
        var calendarEndUtc = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(7), calendarZone);
        var upcoming = await activeTenders
            .Where(tender => tender.SubmissionDeadline >= utcNow && tender.SubmissionDeadline < calendarEndUtc)
            .OrderBy(tender => tender.SubmissionDeadline).ThenBy(tender => tender.Id)
            .Select(tender => new
            {
                tender.Id, tender.Title, tender.SubmissionDeadline,
                OfferCount = tender.Bids.Count(bid => bid.SubmittedAt.HasValue)
            }).ToListAsync(cancellationToken);

        var upcomingByDay = upcoming.ToLookup(tender =>
            TimeZoneInfo.ConvertTimeFromUtc(tender.SubmissionDeadline, calendarZone).Date);
        var closingDays = new List<SourcingClosingDay>();
        for (var offset = 0; offset < 7; offset++)
        {
            var date = today.AddDays(offset);
            var closings = new List<SourcingTenderClosing>();
            foreach (var tender in upcomingByDay[date])
            {
                var localDeadline = TimeZoneInfo.ConvertTimeFromUtc(tender.SubmissionDeadline, calendarZone);
                var timeLeft = tender.SubmissionDeadline - utcNow;
                closings.Add(new SourcingTenderClosing(tender.Id, tender.Title, localDeadline, timeLeft, tender.OfferCount));
            }

            closingDays.Add(new SourcingClosingDay(date, closings.ToArray()));
        }

        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            recentRequests = recentRequests.Where(row => row.RequestNumber.Contains(search) || row.Title.Contains(search)
                || row.Department.Name.Contains(search) || row.Priority.Contains(search) || row.Status.Contains(search));
        }

        var statusCounts = await _db.PurchaseRequisitions.AsNoTracking()
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

        return View(new SourcingDashboardViewModel
        {
            ClosingDays = closingDays.ToArray(),
            RequestStatuses =
            [
                ("Submitted", statusCounts.GetValueOrDefault("Submitted"), "submitted"),
                ("Under Review", statusCounts.GetValueOrDefault("Under Review"), "review"),
                ("Approved", statusCounts.GetValueOrDefault("Approved"), "approved"),
                ("Returned", statusCounts.GetValueOrDefault("Returned"), "returned"),
                ("Rejected", statusCounts.GetValueOrDefault("Rejected"), "rejected"),
                ("Converted to Tender", statusCounts.GetValueOrDefault("Converted to Tender"), "converted")
            ],
            PendingInvitationCount = await _db.Invitations.CountAsync(invitation => invitation.Status == Statuses.InvitationStatus.Pending, cancellationToken),
            ApprovedRequestCount = await approvedRequests.CountAsync(cancellationToken),
            ActiveTenderCount = await activeTenders.CountAsync(cancellationToken),
            IncomingOfferCount = await incomingOffers.CountAsync(cancellationToken),
            RecentRequests = await recentRequests.LatestActivityFirst(_db).Take(PreviewLimit)
                .Select(request => new SourcingDashboardRow(request.Id, request.RequestNumber, request.Title,
                    request.SubmittedAt ?? request.CreatedAt, request.Priority, request.Status))
                .ToListAsync(cancellationToken)
        });
    }
}
