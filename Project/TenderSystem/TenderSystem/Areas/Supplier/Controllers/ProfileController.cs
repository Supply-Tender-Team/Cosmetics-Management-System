using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier)]
public class ProfileController : Controller
{
    // OWNER: Sara

    public IActionResult Index() => View();
}
