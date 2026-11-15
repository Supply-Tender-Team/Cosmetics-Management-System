using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.EvaluationCommittee)]
public class CommitteeBriefController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(int id)
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tender = await db.Tenders.AsNoTracking()
            .Include(t => t.Requisition).Include(t => t.EvaluationCriteria)
            .FirstOrDefaultAsync(t => t.Id == id && t.PublishDate != null && t.Status != "Draft"
                && t.CommitteeMembers.Any(member => member.UserId == memberId));
        return tender is null ? NotFound() : View(tender);
    }
}
