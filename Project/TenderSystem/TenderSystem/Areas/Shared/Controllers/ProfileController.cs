using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class ProfileController : Controller
{
    // OWNER: Sara

    private readonly UserManager<ApplicationUser> _users;
    private readonly PasskeyService _passkeys;
    private readonly FaceService _face;

    public ProfileController(UserManager<ApplicationUser> users, PasskeyService passkeys, FaceService face)
    {
        _users = users;
        _passkeys = passkeys;
        _face = face;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        ViewData["Devices"] = await _passkeys.ListAsync(userId);
        ViewData["Faces"] = await _face.CountAsync(userId);
        ViewData["Reload"] = true;
        return View();
    }
}
