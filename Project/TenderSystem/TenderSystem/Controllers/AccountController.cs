using Microsoft.AspNetCore.Mvc;

namespace TenderSystem.Controllers;

// OWNER: Dev 2 (login, passkeys, face login)  +  Dev 3 (supplier registration)
public class AccountController : Controller
{
    // GET /Account/Login  - email+password, "Sign in with fingerprint", "Sign in with face"
    public IActionResult Login() => View();

    // GET /Account/RegisterSupplier
    public IActionResult RegisterSupplier() => View();

    public IActionResult AccessDenied() => View();

    // POST /Account/Logout (the navbar and dashboard header post to this)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        // TODO (Dev 2): await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // TODO (Dev 2): Passkey/Options, Passkey/Verify, Face/Verify endpoints go here.
}
