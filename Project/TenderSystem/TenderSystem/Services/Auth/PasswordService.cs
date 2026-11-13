using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Email;

namespace TenderSystem.Services.Auth;

/// <summary>
/// All password operations in one place:
///   ChangePasswordAsync   - a signed-in user changes his password (onboarding screen and Profile page call this)
///   SendResetLinkAsync    - "forgot password": emails a one-time reset link
///   ResetPasswordAsync    - the user opened the link and chose a new password
///
/// Controllers only collect the form and show the result; the rules live here.
/// Nothing here ever tells the caller whether an email address exists (no account guessing).
/// </summary>
public class PasswordService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly EmailService _email;
    private readonly ILogger<PasswordService> _log;

    public PasswordService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        EmailService email, ILogger<PasswordService> log)
    {
        _users = users;
        _signIn = signIn;
        _email = email;
        _log = log;
    }

    private static IdentityResult Fail(string code, string message) =>
        IdentityResult.Failed(new IdentityError { Code = code, Description = message });

    // ───────────────────────── change (user is signed in) ─────────────────────────

    /// <summary>
    /// Checks the current password and sets the new one. On success the "must change password" flag is cleared,
    /// onboarding moves from NotStarted to InProgress, the user stays signed in, and a notice email is sent.
    /// Usage: var result = await _passwords.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword);
    ///        if (!result.Succeeded) foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
    /// </summary>
    public async Task<IdentityResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null) return Fail("UserNotFound", "User not found.");

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded) return result;

        user.MustChangePassword = false;
        if (user.OnboardingStatus == Statuses.OnboardingStatus.NotStarted)
            user.OnboardingStatus = Statuses.OnboardingStatus.InProgress;
        await _users.UpdateAsync(user);

        // The security stamp changed, so refresh the cookie to keep this user signed in.
        await _signIn.RefreshSignInAsync(user);

        // TODO (Marwa's AuditService): audit "PasswordChanged".
        await SendChangedNoticeAsync(user);
        return result;
    }

    // ───────────────────────── forgot (user is NOT signed in) ─────────────────────────

    /// <summary>
    /// Sends the reset email if the address belongs to an account. Returns nothing on purpose.
    /// <paramref name="buildPath"/> turns (email, token) into the page address, for example:
    ///   (e, t) => Url.Action("ResetPassword", "Account", new { email = e, token = t })!
    /// </summary>
    public async Task SendResetLinkAsync(string email, Func<string, string, string> buildPath)
    {
        var user = await _users.FindByEmailAsync(email.Trim());
        if (user == null)
        {
            _log.LogInformation("Password reset asked for an unknown address.");
            return;
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = _email.AbsoluteUrl(buildPath(user.Email!, encoded));

        await _email.SendAsync(user.Email!, "Reset your password", EmailTemplates.PasswordReset(user.FullName, link));
    }

    // ───────────────────────── reset (opened the email link) ─────────────────────────

    /// <summary>Sets a new password with the token from the email link. A token works once and expires after 1 hour.</summary>
    public async Task<IdentityResult> ResetPasswordAsync(string email, string encodedToken, string newPassword)
    {
        var invalid = Fail("InvalidToken", "This link is invalid or has expired. Please request a new one.");

        var user = await _users.FindByEmailAsync(email.Trim());
        if (user == null) return invalid;

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken)); }
        catch (FormatException) { return invalid; }

        var result = await _users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code == "InvalidToken") ? invalid : result;

        // A reset also unlocks the account and removes the "temporary password" flag.
        user.MustChangePassword = false;
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
        await _users.UpdateAsync(user);

        // TODO (Marwa's AuditService): audit "PasswordReset".
        await SendChangedNoticeAsync(user);
        return result;
    }

    private Task SendChangedNoticeAsync(ApplicationUser user) =>
        _email.SendAsync(user.Email!, "Your password was changed",
            EmailTemplates.PasswordChanged(user.FullName, _email.AbsoluteUrl("/Account/Login")));
}
