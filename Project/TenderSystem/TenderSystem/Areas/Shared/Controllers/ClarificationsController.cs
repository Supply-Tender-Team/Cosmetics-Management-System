using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class ClarificationsController : Controller
{
    // OWNER: Deyaa

    public IActionResult Index() => View();

    public IActionResult Create() => View();

    public IActionResult Details() => View();
}
