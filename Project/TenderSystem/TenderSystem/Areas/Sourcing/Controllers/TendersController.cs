using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class TendersController : Controller
{
    // OWNER: Dev 4

    public IActionResult Index() => View();

    public IActionResult Create() => View();

    public IActionResult Suppliers() => View();
}
