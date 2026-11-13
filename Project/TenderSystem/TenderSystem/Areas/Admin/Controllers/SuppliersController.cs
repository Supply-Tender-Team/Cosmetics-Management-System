using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class SuppliersController : Controller
{
    // OWNER: Dev 3

    public IActionResult Index() => View();

    public IActionResult Review() => View();
}
