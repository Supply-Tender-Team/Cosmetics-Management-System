using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class RequestsController : Controller
{
    // OWNER: Nada

    public IActionResult Index() => View();

    public IActionResult Review() => View();
}
