using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class RequestsController(ApplicationDbContext db, AuditService audit) : Controller
{
    public async Task<IActionResult> Index()
    {
        var requests = await db.PurchaseRequisitions.AsNoTracking()
            .Include(request => request.Department)
            .Include(request => request.RequestedByUser)
            .Where(request => request.Status == "Submitted" || request.Status == "Under Review")
            .OrderByDescending(request => request.SubmittedAt)
            .ThenByDescending(request => request.Id)
            .ToListAsync();
        return View(requests);
    }

    public async Task<IActionResult> Review(int id, bool popup = false)
    {
        var request = await db.PurchaseRequisitions.AsNoTracking()
            .Include(request => request.Department)
            .Include(request => request.RequestedByUser)
            .Include(request => request.PurchaseRequisitionItems)
            .FirstOrDefaultAsync(request => request.Id == id && request.Status != "Draft");
        if (request is null) return NotFound();
        if (popup)
        {
            ViewData["Popup"] = true;
            return PartialView("_ReviewContent", request);
        }
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int id, string decision, string? note, bool popup = false)
    {
        var status = decision switch
        {
            "Approve" => "Approved",
            "Return" => "Returned",
            "Reject" => "Rejected",
            _ => null
        };
        if (status is null) return BadRequest();

        var reviewer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (reviewer is null) return Forbid();

        note = note?.Trim();
        if (!ModelState.IsValid || note?.Length > 1000 || (status == "Returned" && string.IsNullOrWhiteSpace(note)))
        {
            TempData["RequestError"] = "Enter a review note for a return. Notes can contain up to 1,000 characters.";
            return popup ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id });
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var updated = await db.PurchaseRequisitions
            .Where(request => request.Id == id && (request.Status == "Submitted" || request.Status == "Under Review"))
            .ExecuteUpdateAsync(update => update
                .SetProperty(request => request.Status, status)
                .SetProperty(request => request.ReviewedByUserId, reviewer)
                .SetProperty(request => request.ReviewNote, note));

        if (updated == 0)
        {
            TempData["RequestError"] = "The request is no longer awaiting review. Your decision was not saved.";
            return RedirectToAction(nameof(Index));
        }

        await audit.LogAsync(status, "PurchaseRequisition", id.ToString());
        await transaction.CommitAsync();
        TempData["RequestSuccess"] = $"Purchase request {status.ToLowerInvariant()}.";
        return popup ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id });
    }
}

