using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
// [Authorize]  // TODO: enable when login works (any signed-in user)
public class ClarificationsController : Controller
{
    // OWNER: Dev 2 / Dev 7

    public IActionResult Index() => View();

    public IActionResult Create() => View();

    public IActionResult Details() => View();
}
