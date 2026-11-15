using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Controllers;

[Area("Committee")]
[Authorize(Roles = Roles.TopCommittee)]
public class DecisionsController : Controller
{
    // OWNER: Dana

    public IActionResult Index() => RedirectToAction("Index", "Votes", new { area = "Committee" });
}
