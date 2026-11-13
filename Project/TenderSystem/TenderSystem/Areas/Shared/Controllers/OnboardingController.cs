using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
// [Authorize]  // TODO: enable when login works (any signed-in user)
public class OnboardingController : Controller
{
    // OWNER: Dev 2 / Dev 7

    public IActionResult ChangePassword() => View();

    public IActionResult Profile() => View();

    public IActionResult Fingerprint() => View();

    public IActionResult Face() => View();

    public IActionResult Complete() => View();
}
