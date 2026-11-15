using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class ComplianceController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(int? tenderId)
    {
        ViewData["TenderId"] = tenderId;
        var bids = await db.Bids.AsNoTracking().AsSplitQuery()
            .Include(bid => bid.Tender)
            .Include(bid => bid.SupplierProfile)
            .Include(bid => bid.BidItems).ThenInclude(item => item.RequisitionItem)
            .Include(bid => bid.ComplianceChecks)
            .Where(bid => (tenderId == null || bid.TenderId == tenderId) &&
                (bid.Status == Statuses.BidStatus.Submitted || bid.Status == Statuses.BidStatus.UnderReview ||
                 bid.Status == Statuses.BidStatus.Compliant || bid.Status == Statuses.BidStatus.NonCompliant))
            .OrderByDescending(bid => bid.SubmittedAt).ToListAsync();
        return View(bids);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, string result, string? reason)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (reviewerId is null) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var offer = await db.Bids.Where(bid => bid.Id == id)
            .Select(bid => new { bid.TenderId }).SingleOrDefaultAsync();
        if (offer is null) return NotFound();
        var destination = RedirectToAction(nameof(Index), new { tenderId = offer.TenderId });
        reason = reason?.Trim();
        if (result is not (Statuses.ComplianceResult.Compliant or Statuses.ComplianceResult.NonCompliant) ||
            reason?.Length > 1000 || (result == Statuses.ComplianceResult.NonCompliant && string.IsNullOrEmpty(reason)))
        {
            TempData["ComplianceError"] = "Enter a valid decision and a reason for a non-compliant offer.";
            return destination;
        }

        var changed = await db.Bids.Where(bid => bid.Id == id &&
                (bid.Status == Statuses.BidStatus.Submitted || bid.Status == Statuses.BidStatus.UnderReview))
            .ExecuteUpdateAsync(setters => setters.SetProperty(bid => bid.Status, result));
        if (changed == 0)
        {
            TempData["ComplianceError"] = "This offer has already been reviewed or is no longer available.";
            return destination;
        }

        db.ComplianceChecks.Add(new ComplianceCheck
        {
            BidId = id, CheckedByUserId = reviewerId, Result = result, Reason = reason
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["ComplianceSuccess"] = "Compliance decision saved.";
        return destination;
    }
}