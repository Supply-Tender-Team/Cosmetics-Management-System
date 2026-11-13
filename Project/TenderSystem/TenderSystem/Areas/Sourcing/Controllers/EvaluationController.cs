using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class EvaluationController : Controller
{
    // OWNER: Deyaa

    public IActionResult Index() => View();
}
