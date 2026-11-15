using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee)]
public class TechnicalController(ApplicationDbContext db) : Controller
{
    // OWNER: Marwa

    public async Task<IActionResult> Index()
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (memberId is null) return Forbid();

        var shares = await db.Notifications.AsNoTracking()
            .Where(notification => notification.UserId == memberId && notification.Type == "TenderEvaluation"
                && notification.Link != null)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .ToListAsync();

        return View(shares.DistinctBy(notification => notification.Link).ToArray());
    }
}
