using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class OfferPreparationController : Controller
{
    private readonly ApplicationDbContext _db;

    public OfferPreparationController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Create(int tenderId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var supplierId = await _db.SupplierProfiles.AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierId is null) return Forbid();

        var now = DateTime.UtcNow;
        var tender = await _db.Tenders.AsNoTracking()
            .Include(item => item.Requisition)
            .ThenInclude(requisition => requisition.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(item => item.Id == tenderId
                && item.SubmissionDeadline > now
                && (item.Status == Statuses.TenderStatus.Published || item.Status == Statuses.TenderStatus.Open)
                && item.Invitations.Any(invitation => invitation.SupplierProfileId == supplierId.Value
                    && invitation.Status == Statuses.InvitationStatus.Accepted), cancellationToken);
        if (tender is null) return NotFound("This tender is no longer available for offer preparation.");

        var existingBid = await _db.Bids.AsNoTracking()
            .Include(bid => bid.BidItems)
            .SingleOrDefaultAsync(bid => bid.TenderId == tenderId && bid.SupplierProfileId == supplierId.Value, cancellationToken);
        if (existingBid is not null && existingBid.Status != "Draft")
            return StatusCode(StatusCodes.Status409Conflict, "An offer has already been submitted for this tender.");

        var existingItems = existingBid?.BidItems.ToDictionary(item => item.RequisitionItemId)
            ?? new Dictionary<int, BidItem>();
        var model = new CreateSupplierOfferViewModel
        {
            TenderId = tender.Id,
            TenderNumber = tender.TenderNumber,
            TenderTitle = tender.Title,
            SubmissionDeadline = tender.SubmissionDeadline,
            Currency = existingBid?.Currency ?? "USD",
            DeliveryDays = existingBid?.DeliveryDays ?? 0,
            ValidityDays = existingBid?.ValidityDays ?? 0,
            PaymentTerms = existingBid?.PaymentTerms ?? string.Empty,
            TechnicalSummary = existingBid?.TechnicalSummary ?? string.Empty,
            Items = tender.Requisition.PurchaseRequisitionItems.OrderBy(item => item.Id).Select(item =>
            {
                existingItems.TryGetValue(item.Id, out var bidItem);
                return new SupplierOfferItemInputViewModel
                {
                    RequisitionItemId = item.Id,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    RequestedSpecifications = item.Specifications,
                    UnitPrice = bidItem?.UnitPrice,
                    OfferedSpecifications = bidItem?.OfferedSpecifications ?? string.Empty
                };
            }).ToList()
        };

        ViewData["OfferFormInModal"] = true;
        return PartialView("~/Areas/Supplier/Views/Offers/_PrepareOfferForm.cshtml", model);
    }
}
