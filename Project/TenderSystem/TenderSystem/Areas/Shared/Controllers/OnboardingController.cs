using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class OnboardingController : Controller
{
    // OWNER: Sara

    public IActionResult ChangePassword() => View();

    public IActionResult Profile() => View();

    public IActionResult Fingerprint() => View();

    public IActionResult Face() => View();

    public IActionResult Complete() => View();
}
