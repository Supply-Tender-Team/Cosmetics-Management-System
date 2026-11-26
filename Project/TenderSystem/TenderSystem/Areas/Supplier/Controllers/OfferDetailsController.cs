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
        var supplierUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(supplierUserId)) return Challenge();

        var offer = await _db.Bids.AsNoTracking()
            .Where(offer => offer.Id == id && offer.SupplierProfile.UserId == supplierUserId)
            .Select(offer => new SupplierOfferDetailsViewModel
            {
                Id = offer.Id,
                TenderId = offer.TenderId,
                TenderNumber = offer.Tender.TenderNumber,
                TenderTitle = offer.Tender.Title,
                Status = offer.Status,
                WasSelected = offer.Tender.Status == Statuses.TenderStatus.Awarded
                    && offer.Tender.AwardedSupplierId.HasValue
                    && offer.SubmittedAt.HasValue && offer.Status != Statuses.BidStatus.Draft
                    && offer.Status != Statuses.BidStatus.Withdrawn
                    && _db.AuditLogs.Any(log => log.Action == "SupplierAwardResultsSent"
                        && log.EntityName == "Tender" && log.EntityId == offer.TenderId.ToString())
                    ? offer.SupplierProfileId == offer.Tender.AwardedSupplierId
                    : null,
                TotalPrice = offer.TotalPrice,
                Currency = offer.Currency,
                DeliveryDays = offer.DeliveryDays,
                ValidityDays = offer.ValidityDays,
                PaymentTerms = offer.PaymentTerms,
                TechnicalSummary = offer.TechnicalSummary,
                SubmissionDeadline = offer.Tender.SubmissionDeadline,
                SubmittedAt = offer.SubmittedAt,
                Items = offer.BidItems.OrderBy(item => item.Id).Select(item => new SupplierOfferDetailsItemRow
                {
                    Id = item.Id,
                    ItemName = item.RequisitionItem.ItemName,
                    Quantity = item.Quantity,
                    Unit = item.RequisitionItem.Unit,
                    UnitPrice = item.UnitPrice,
                    TaxRate = item.TaxRate,
                    TaxAmount = item.TaxAmount,
                    ShippingCost = item.ShippingCost,
                    InstallationCost = item.InstallationCost ?? 0m,
                    LineTotal = item.UnitPrice * item.Quantity + item.TaxAmount + item.ShippingCost + (item.InstallationCost ?? 0m),
                    OfferedSpecifications = item.OfferedSpecifications
                }).ToList(),
                Documents = offer.BidDocuments.OrderBy(document => document.FileName).Select(document => new SupplierOfferDetailsDocumentRow
                {
                    Id = document.Id,
                    FileName = document.FileName,
                    SizeBytes = document.SizeBytes,
                    DocType = document.DocType,
                    BidItemId = document.BidItemId
                }).ToList(),
                Guarantees = offer.BidGuarantees.OrderBy(guarantee => guarantee.GuaranteeType).Select(guarantee => new SupplierOfferDetailsGuaranteeRow
                {
                    GuaranteeType = guarantee.GuaranteeType,
                    BeneficiaryName = guarantee.BeneficiaryName,
                    Amount = guarantee.Amount,
                    Currency = guarantee.Currency,
                    ReferenceNumber = guarantee.ReferenceNumber,
                    IssuerName = guarantee.IssuerName,
                    ExpiresAt = guarantee.ExpiresAt
                }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (offer is null) return NotFound();

        return string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
            ? PartialView("_DetailsModal", offer)
            : View(offer);
    }
}
