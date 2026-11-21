using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer + "," + Roles.Supplier)]
public class TenderNoticeController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = db.Tenders.AsNoTracking().Where(t => t.Id == id);
        if (!User.IsInRole(Roles.SourcingOfficer))
            query = query.Where(t => t.PublishDate.HasValue && t.Status != Statuses.TenderStatus.Draft
                && t.Invitations.Any(i => i.SupplierProfile.UserId == userId));

        // Only the supplier-facing notice is loaded. Evaluation criteria and preferences stay internal.
        var model = await query.Select(t => new TenderSystem.Areas.Sourcing.Models.TenderNotice
        {
            Id = t.Id, Number = t.TenderNumber, Title = t.Title, Description = t.Description,
            Terms = t.Terms, Deadline = t.SubmissionDeadline, Department = t.Requisition.Department.Name,
            Items = t.Requisition.PurchaseRequisitionItems.OrderBy(i => i.Id)
                .Select(i => new TenderSystem.Areas.Sourcing.Models.TenderNoticeItem
                { Name = i.ItemName, Quantity = i.Quantity, Unit = i.Unit, Specifications = i.Specifications }).ToList()
        }).SingleOrDefaultAsync();
        return model is null ? NotFound() : View(model);
    }
}
