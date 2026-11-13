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
public class InvitationsController : Controller
{
    // OWNER: Dev 5

    private readonly ApplicationDbContext _db;
    public InvitationsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var supplierId = await _db.SupplierProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierId is null)
            return View(Array.Empty<SupplierInvitationRow>());

        var invitations = await _db.Invitations.AsNoTracking()
            .Where(x => x.SupplierProfileId == supplierId)
            .OrderByDescending(x => x.SentAt)
            .Select(x => new SupplierInvitationRow
            {
                Id = x.Id,
                TenderId = x.TenderId,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                ExpiresAt = x.ExpiresAt
            })
            .ToListAsync(cancellationToken);
        return View(invitations);
    }
}
