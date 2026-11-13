using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class OffersController : Controller
{
    // OWNER: Dev 5

    public IActionResult Index() => View();

    public IActionResult Create() => View();
}
