using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class OfferDetailsController : Controller
{
    private readonly ApplicationDbContext _db;

    public OfferDetailsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var offer = await _db.Bids.AsNoTracking()
            .Where(x => x.Id == id && x.SupplierProfile.UserId == userId)
            .Select(x => new SupplierOfferDetailsViewModel
            {
                Id = x.Id,
                TenderId = x.TenderId,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                WasSelected = x.Tender.Status == Statuses.TenderStatus.Awarded
                    && x.Tender.AwardedSupplierId.HasValue
                    && x.SubmittedAt.HasValue && x.Status != Statuses.BidStatus.Draft
                    && x.Status != Statuses.BidStatus.Withdrawn
                    && _db.AuditLogs.Any(log => log.Action == "SupplierAwardResultsSent"
                        && log.EntityName == "Tender" && log.EntityId == x.TenderId.ToString())
                    ? x.SupplierProfileId == x.Tender.AwardedSupplierId
                    : null,
                TotalPrice = x.TotalPrice,
                Currency = x.Currency,
                DeliveryDays = x.DeliveryDays,
                ValidityDays = x.ValidityDays,
                PaymentTerms = x.PaymentTerms,
                TechnicalSummary = x.TechnicalSummary,
                SubmissionDeadline = x.Tender.SubmissionDeadline,
                SubmittedAt = x.SubmittedAt,
                Items = x.BidItems.OrderBy(item => item.Id).Select(item => new SupplierOfferDetailsItemRow
                {
                    ItemName = item.RequisitionItem.ItemName,
                    Quantity = item.Quantity,
                    Unit = item.RequisitionItem.Unit,
                    UnitPrice = item.UnitPrice,
                    TaxAmount = item.TaxAmount,
                    ShippingCost = item.ShippingCost,
                    InstallationCost = item.InstallationCost ?? 0m,
                    LineTotal = item.UnitPrice * item.Quantity + item.TaxAmount + item.ShippingCost + (item.InstallationCost ?? 0m),
                    OfferedSpecifications = item.OfferedSpecifications
                }).ToList(),
                Documents = x.BidDocuments.OrderBy(document => document.FileName).Select(document => new SupplierOfferDetailsDocumentRow
                {
                    Id = document.Id,
                    FileName = document.FileName,
                    SizeBytes = document.SizeBytes
                }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return offer is null ? NotFound() : View(offer);
    }
}
