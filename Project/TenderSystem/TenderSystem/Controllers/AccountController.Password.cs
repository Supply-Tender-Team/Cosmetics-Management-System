using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Models;
using TenderSystem.Services.Files;

namespace TenderSystem.Controllers;

// OWNER: Wadea. Forgot password and reset password (the e-mail with the link is sent by PasswordService).
// The same AccountController (a partial class): the fields and the constructor are in AccountController.cs.
public partial class AccountController
{
    // ───────────────────────── Forgot / reset password ─────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // The answer is the same whether or not the address exists, so nobody can probe for accounts.
        await _passwords.SendResetLinkAsync(model.Email,
            (email, token) => Url.Action(nameof(ResetPassword), "Account", new { email, token })!);

        return View("ForgotPasswordSent");
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token) =>
        View(new ResetPasswordViewModel { Email = email ?? string.Empty, Token = token ?? string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _passwords.ResetPasswordAsync(model.Email, model.Token, model.Password);
        if (result.Succeeded)
        {
            TempData["Success"] = "Your password was changed. You can sign in now.";
            return RedirectToAction(nameof(Login));
        }

        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }
}
