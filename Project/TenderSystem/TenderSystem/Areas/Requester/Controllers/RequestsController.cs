using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Requester.Controllers;

[Area("Requester")]
[Authorize(Roles = Roles.Requester)]
public class RequestsController : Controller
{
    // OWNER: Dana

    public IActionResult Index() => View();

    public IActionResult Create() => View();

    public IActionResult Details() => View();
}
