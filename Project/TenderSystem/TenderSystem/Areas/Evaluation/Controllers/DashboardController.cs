using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee)]
public class DashboardController : Controller
{
    // OWNER: Marwa

    public IActionResult Index() => View();
}
