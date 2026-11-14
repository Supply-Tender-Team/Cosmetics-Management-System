using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class DashboardController : Controller
{
    // OWNER: Dev 5

    private readonly ApplicationDbContext _db;
    public DashboardController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var supplier = await _db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (supplier is null)
            return View("ProfileMissing");

        var bids = _db.Bids.AsNoTracking().Where(x => x.SupplierProfileId == supplier.Id);
        var invitations = await _db.Invitations.AsNoTracking()
            .Where(x => x.SupplierProfileId == supplier.Id)
            .OrderByDescending(x => x.SentAt)
            .Take(5)
            .Select(x => new SupplierInvitationRow
            {
                Id = x.Id,
                TenderId = x.TenderId,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                ExpiresAt = x.ExpiresAt
            }).ToListAsync(cancellationToken);
        var offers = await _db.Bids.AsNoTracking()
            .Where(x => x.SupplierProfileId == supplier.Id)
            .OrderByDescending(x => x.SubmittedAt)
            .Take(5)
            .Select(x => new SupplierOfferRow
            {
                Id = x.Id,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                TotalPrice = x.TotalPrice,
                Currency = x.Currency,
                SubmittedAt = x.SubmittedAt
            }).ToListAsync(cancellationToken);

        var model = new SupplierDashboardViewModel
        {
            SupplierName = supplier.CompanyName,
            VerificationStatus = supplier.Status,
            PendingInvitations = await _db.Invitations.CountAsync(x => x.SupplierProfileId == supplier.Id && x.Status == "Pending", cancellationToken),
            SubmittedOffers = await bids.CountAsync(x => x.Status != "Draft", cancellationToken),
            OffersUnderReview = await bids.CountAsync(x => x.Status == "Under Review" || x.Status == "Compliant" || x.Status == "Non-Compliant", cancellationToken),
            ApprovedOffers = await bids.CountAsync(x => x.Status == "Accepted", cancellationToken),
            RecentInvitations = invitations,
            RecentOffers = offers
        };

        return View(model);
    }
}
