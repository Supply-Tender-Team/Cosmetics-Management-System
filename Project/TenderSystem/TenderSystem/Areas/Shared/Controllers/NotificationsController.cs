using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class NotificationsController : Controller
{
    // OWNER: Deyaa

    public IActionResult Index() => View();
}
