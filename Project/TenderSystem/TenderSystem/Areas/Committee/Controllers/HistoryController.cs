using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Controllers;

[Area("Committee")]
// [Authorize(Roles = Roles.TopCommittee)]  // TODO: enable when login works
public class HistoryController : Controller
{
    // OWNER: Dev 6

    public IActionResult Index() => View();
}
