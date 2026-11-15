using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class TechnicalController : Controller
{
    // OWNER: Marwa

    public IActionResult Index()
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (memberId is null) return Forbid();

        return RedirectToAction("Index", "Evaluation");
    }
}
