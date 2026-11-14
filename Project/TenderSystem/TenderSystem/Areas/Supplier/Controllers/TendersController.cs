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
public class TendersController : Controller
{
    // OWNER: Dev 5

    private const string AcceptedInvitationStatus = Statuses.InvitationStatus.Accepted;
    private const string PublishedTenderStatus = Statuses.TenderStatus.Published;
    private const string OpenTenderStatus = Statuses.TenderStatus.Open;

    private readonly ApplicationDbContext _db;

    public TendersController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var supplierId = await _db.SupplierProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierId is null) return Forbid();

        var tender = await _db.Tenders.AsNoTracking()
            .Where(x => x.Id == id && x.Invitations.Any(invitation => invitation.SupplierProfileId == supplierId))
            .Select(x => new SupplierTenderDetailsViewModel
            {
                Id = x.Id,
                TenderNumber = x.TenderNumber,
                Title = x.Title,
                Description = x.Description,
                Terms = x.Terms,
                Status = x.Status,
                SubmissionDeadline = x.SubmissionDeadline,
                InvitationStatus = x.Invitations
                    .Where(invitation => invitation.SupplierProfileId == supplierId)
                    .OrderByDescending(invitation => invitation.SentAt)
                    .Select(invitation => invitation.Status)
                    .FirstOrDefault() ?? string.Empty,
                InvitationExpiresAt = x.Invitations
                    .Where(invitation => invitation.SupplierProfileId == supplierId)
                    .OrderByDescending(invitation => invitation.SentAt)
                    .Select(invitation => (DateTime?)invitation.ExpiresAt)
                    .FirstOrDefault(),
                CanSubmitOffer = x.SubmissionDeadline > DateTime.UtcNow
                    && (x.Status == PublishedTenderStatus || x.Status == OpenTenderStatus)
                    && x.Invitations.Any(invitation => invitation.SupplierProfileId == supplierId && invitation.Status == AcceptedInvitationStatus),
                Items = x.Requisition.PurchaseRequisitionItems
                    .OrderBy(item => item.Id)
                    .Select(item => new SupplierTenderItemViewModel
                    {
                        ItemName = item.ItemName,
                        Quantity = item.Quantity,
                        Unit = item.Unit,
                        Specifications = item.Specifications
                    }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return tender is null ? NotFound() : View(tender);
    }
}
