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
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        var supplierProfileId = await _db.SupplierProfiles.AsNoTracking()
            .Where(profile => profile.UserId == supplierUserId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierProfileId is null) return Forbid();

        var tender = await _db.Tenders.AsNoTracking()
            .Where(tender => tender.Id == id
                && tender.Invitations.Any(invitation => invitation.SupplierProfileId == supplierProfileId))
            .Select(tender => new SupplierTenderDetailsViewModel
            {
                Id = tender.Id,
                TenderNumber = tender.TenderNumber,
                Title = tender.Title,
                Description = tender.Description,
                Terms = tender.Terms,
                Status = tender.Status,
                SubmissionDeadline = tender.SubmissionDeadline,
                InvitationStatus = tender.Invitations
                    .Where(invitation => invitation.SupplierProfileId == supplierProfileId)
                    .OrderByDescending(invitation => invitation.SentAt)
                    .Select(invitation => invitation.Status)
                    .FirstOrDefault() ?? string.Empty,
                InvitationExpiresAt = tender.Invitations
                    .Where(invitation => invitation.SupplierProfileId == supplierProfileId)
                    .OrderByDescending(invitation => invitation.SentAt)
                    .Select(invitation => (DateTime?)invitation.ExpiresAt)
                    .FirstOrDefault(),
                CanSubmitOffer = tender.SubmissionDeadline > DateTime.UtcNow
                    && (tender.Status == PublishedTenderStatus || tender.Status == OpenTenderStatus)
                    && tender.Invitations.Any(invitation => invitation.SupplierProfileId == supplierProfileId && invitation.Status == AcceptedInvitationStatus),
                Items = tender.Requisition.PurchaseRequisitionItems
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
