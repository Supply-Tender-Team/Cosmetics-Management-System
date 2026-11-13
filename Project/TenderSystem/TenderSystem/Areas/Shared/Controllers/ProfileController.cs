using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class ProfileController : Controller
{
    // OWNER: Sara

    public IActionResult Index() => View();
}
