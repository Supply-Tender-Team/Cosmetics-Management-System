using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
// [Authorize(Roles = Roles.SourcingOfficer)]  // TODO: enable when login works
public class RequestsController : Controller
{
    // OWNER: Dev 4

    public IActionResult Index() => View();

    public IActionResult Review() => View();
}
