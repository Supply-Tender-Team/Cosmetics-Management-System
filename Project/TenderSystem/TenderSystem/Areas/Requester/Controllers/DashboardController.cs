using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Requester.Controllers;

[Area("Requester")]
[Authorize(Roles = Roles.Requester)]
public class DashboardController : Controller
{
    // OWNER: Dana

    public IActionResult Index() => View();
}
