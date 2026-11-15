using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class OffersController(ApplicationDbContext db) : Controller
{
    // OWNER: Deyaa

    public async Task<IActionResult> Compare(int? tenderId)
    {
        var offers = await db.Bids.AsNoTracking().Include(bid => bid.Tender).Include(bid => bid.SupplierProfile)
            .Where(bid => bid.SubmittedAt != null && (tenderId == null || bid.TenderId == tenderId)
                && bid.Status != Statuses.BidStatus.Draft && bid.Status != Statuses.BidStatus.Withdrawn)
            .OrderByDescending(bid => bid.SubmittedAt).ToListAsync();
        return View(offers);
    }
}
