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
        var requests = _db.PurchaseRequisitions.AsNoTracking()
            .Where(request => request.Status == Statuses.RequestStatus.Approved);
        var tenders = _db.Tenders.AsNoTracking()
            .Where(tender => tender.Status == Statuses.TenderStatus.Published
                || tender.Status == Statuses.TenderStatus.Open);
        var offers = _db.Bids.AsNoTracking()
            .Where(bid => bid.SubmittedAt.HasValue && (bid.Status == Statuses.BidStatus.Submitted
                || bid.Status == Statuses.BidStatus.UnderReview));

        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            requests = requests.Where(row => row.RequestNumber.Contains(search) || row.Title.Contains(search)
                || row.Department.Name.Contains(search) || row.Priority.Contains(search));
            tenders = tenders.Where(row => row.TenderNumber.Contains(search) || row.Title.Contains(search)
                || row.Requisition.RequestNumber.Contains(search) || row.Status.Contains(search));
            offers = offers.Where(row => row.Tender.TenderNumber.Contains(search) || row.Tender.Title.Contains(search)
                || row.SupplierProfile.CompanyName.Contains(search) || row.Status.Contains(search));
        }

        return View(new SourcingDashboardViewModel
        {
            PendingInvitationCount = await _db.Invitations.CountAsync(invitation => invitation.Status == Statuses.InvitationStatus.Pending, cancellationToken),
            ApprovedRequestCount = await requests.CountAsync(cancellationToken),
            ActiveTenderCount = await tenders.CountAsync(cancellationToken),
            IncomingOfferCount = await offers.CountAsync(cancellationToken),
            ApprovedRequests = await requests.OrderBy(request => request.TargetDate).ThenBy(request => request.Id)
                .Take(PreviewLimit)
                .Select(request => new SourcingDashboardRow(request.RequestNumber, request.Title,
                    request.Department.Name, request.TargetDate, request.Priority))
                .ToListAsync(cancellationToken),
            ActiveTenders = await tenders.OrderBy(tender => tender.SubmissionDeadline).ThenBy(tender => tender.Id)
                .Take(PreviewLimit)
                .Select(tender => new SourcingDashboardRow(tender.TenderNumber, tender.Title,
                    tender.Requisition.RequestNumber, tender.SubmissionDeadline, tender.Status))
                .ToListAsync(cancellationToken),
            IncomingOffers = await offers.OrderByDescending(bid => bid.SubmittedAt).ThenByDescending(bid => bid.Id)
                .Take(PreviewLimit)
                .Select(bid => new SourcingDashboardRow(bid.Tender.TenderNumber, bid.Tender.Title,
                    bid.SupplierProfile.CompanyName, bid.SubmittedAt!.Value, bid.Status))
                .ToListAsync(cancellationToken)
        });
    }
}
