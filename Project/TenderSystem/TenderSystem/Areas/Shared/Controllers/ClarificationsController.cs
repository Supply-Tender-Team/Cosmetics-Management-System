using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class ClarificationsController : Controller
{
    // OWNER: Dev 2 / Dev 7

    public IActionResult Index() => View();

    public IActionResult Create() => View();

    public IActionResult Details() => View();
}
