using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
// [Authorize(Roles = Roles.EvaluationCommittee)]  // TODO: enable when login works
public class ReportController : Controller
{
    // OWNER: Dev 6

    public IActionResult Index() => View();
}
