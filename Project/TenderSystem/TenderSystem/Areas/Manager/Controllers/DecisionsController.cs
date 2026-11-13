using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Manager.Controllers;

[Area("Manager")]
// [Authorize(Roles = Roles.SourcingManager)]  // TODO: enable when login works
public class DecisionsController : Controller
{
    // OWNER: Dev 6

    public IActionResult Index() => View();
}
