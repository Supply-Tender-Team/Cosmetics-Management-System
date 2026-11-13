using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Requester.Controllers;

[Area("Requester")]
// [Authorize(Roles = Roles.Requester)]  // TODO: enable when login works
public class DashboardController : Controller
{
    // OWNER: Dev 4

    public IActionResult Index() => View();
}
