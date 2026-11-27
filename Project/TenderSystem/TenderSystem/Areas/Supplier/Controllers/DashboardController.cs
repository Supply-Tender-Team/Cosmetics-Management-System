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
    private readonly ApplicationDbContext _db;
    public DashboardController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var supplierProfile = await _db.SupplierProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.UserId == supplierUserId, cancellationToken);

        if (supplierProfile is null)
            return View("ProfileMissing");

        var supplierOffers = _db.Bids
            .AsNoTracking()
            .Where(offer => offer.SupplierProfileId == supplierProfile.Id);

        var recentInvitations = await _db.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.SupplierProfileId == supplierProfile.Id)
            .OrderByDescending(invitation => invitation.SentAt)
            .Take(5)
            .Select(invitation => new SupplierInvitationRow
            {
                Id = invitation.Id,
                TenderId = invitation.TenderId,
                TenderNumber = invitation.Tender.TenderNumber,
                TenderTitle = invitation.Tender.Title,
                Status = invitation.Status,
                ExpiresAt = invitation.ExpiresAt
            })
            .ToListAsync(cancellationToken);

        var recentOffers = await _db.Bids
            .AsNoTracking()
            .Where(offer => offer.SupplierProfileId == supplierProfile.Id)
            .OrderByDescending(offer => offer.SubmittedAt)
            .Take(5)
            .Select(offer => new SupplierOfferRow
            {
                Id = offer.Id,
                TenderNumber = offer.Tender.TenderNumber,
                TenderTitle = offer.Tender.Title,
                Status = offer.Status,
                TotalPrice = offer.TotalPrice,
                Currency = offer.Currency,
                SubmittedAt = offer.SubmittedAt
            })
            .ToListAsync(cancellationToken);

        // Collect the counts and latest records shown on the dashboard.
        var model = new SupplierDashboardViewModel
        {
            SupplierName = supplierProfile.CompanyName,
            VerificationStatus = supplierProfile.Status,
            PendingInvitations = await _db.Invitations.CountAsync(invitation =>
                invitation.SupplierProfileId == supplierProfile.Id && invitation.Status == "Pending", cancellationToken),
            SubmittedOffers = await supplierOffers.CountAsync(offer => offer.Status != "Draft", cancellationToken),
            OffersUnderReview = await supplierOffers.CountAsync(offer =>
               offer.Status == "Under Review" || offer.Status == "Compliant" || offer.Status == "Non-Compliant", cancellationToken),
            ApprovedOffers = await supplierOffers.CountAsync(offer => offer.Status == "Accepted", cancellationToken),
            RecentInvitations = recentInvitations,
            RecentOffers = recentOffers
        };

        return View(model);
    }
}
