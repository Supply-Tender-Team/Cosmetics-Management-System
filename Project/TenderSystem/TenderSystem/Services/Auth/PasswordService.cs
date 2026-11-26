using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
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
    private readonly AuditService _audit;
    private readonly ILogger<PasswordService> _log;

    public PasswordService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        EmailService email, AuditService audit, ILogger<PasswordService> log)
    {
        _users = users;
        _signIn = signIn;
        _email = email;
        _audit = audit;
        _log = log;
    }

    // ───────────────────────── create an employee (Admin) ─────────────────────────

    /// <summary>
    /// The Admin creates an employee. A random temporary password is generated, the account is marked
    /// "must change password", the Welcome email carries the password (it is never shown on screen or saved in the log)
    /// and the creation is audited. If the email cannot be sent the account is NOT kept, so nobody ends up with an
    /// account and no password.
    /// Usage (Areas/Admin/Controllers/UsersController.Create):
    ///   var (result, userId) = await _passwords.CreateEmployeeAsync(model.FullName, model.Email, model.Role,
    ///                              model.DepartmentId, model.JobTitle, model.PhoneNumber);
    ///   if (!result.Succeeded) foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
    /// The caller checks the department and the role first (it already does).
    /// </summary>
    public async Task<(IdentityResult Result, string? UserId)> CreateEmployeeAsync(
        string fullName, string email, string role, int? departmentId, string? jobTitle, string? phoneNumber)
    {
        email = email.Trim();
        var temporaryPassword = NewTemporaryPassword();

        var user = new ApplicationUser
        {
            UserName = await UniqueUserNameAsync(fullName, email),
            Email = email,
            EmailConfirmed = true,
            FullName = fullName.Trim(),
            UserType = "Employee",
            DepartmentId = departmentId,
            JobTitle = string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            IsActive = true,
            MustChangePassword = true,
            OnboardingStatus = Statuses.OnboardingStatus.NotStarted,
        };

        var created = await _users.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded) return (created, null);

        var inRole = await _users.AddToRoleAsync(user, role);
        if (!inRole.Succeeded)
        {
            await _users.DeleteAsync(user);
            return (inRole, null);
        }

        var sent = await _email.SendAsync(user.Email!, "Your account is ready",
            EmailTemplates.Welcome(user.FullName, user.Email!, temporaryPassword, _email.AbsoluteUrl("/Account/Login")));
        if (!sent)
        {
            await _users.DeleteAsync(user);
            return (Fail("EmailNotSent", "The account was not created because the welcome email could not be sent. Check the SMTP settings and try again."), null);
        }

        await _audit.LogAsync("Created", "ApplicationUser", user.Id, $"Employee account created (role {role})");
        return (IdentityResult.Success, user.Id);
    }

    /// <summary>
    /// The user name of a new employee: the full name WITHOUT spaces ("Omar Officer" becomes "OmarOfficer"). Only letters, digits and . _ - are kept,
    /// because Identity refuses any other character in a user name. A name with nothing usable (for example Arabic letters) uses the first part of the
    /// email instead. If the name is already taken, 2, 3... is added ("OmarOfficer2"). People still sign in with their email.
    /// </summary>
    private async Task<string> UniqueUserNameAsync(string fullName, string email)
    {
        var name = Regex.Replace(fullName.Trim(), "[^A-Za-z0-9._-]", "");
        if (name.Length < 3) name = Regex.Replace(email.Split('@')[0], "[^A-Za-z0-9._-]", "");
        if (name.Length == 0) name = "user";
        if (name.Length > 40) name = name[..40];

        var candidate = name;
        for (var number = 2; await _users.FindByNameAsync(candidate) is not null; number++)
            candidate = name + number;
        return candidate;
    }

    /// <summary>14 random characters with an upper-case letter, a lower-case letter, a digit and a symbol (no look-alike letters).</summary>
    private static string NewTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", symbols = "!@#$%*?";
        const string all = upper + lower + digits + symbols;
        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(symbols) };
        while (chars.Count < 14) chars.Add(Pick(all));
        for (var i = chars.Count - 1; i > 0; i--)                       // shuffle, so the first four are not predictable
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
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

        await _audit.LogForUserAsync(user.Id, "PasswordChanged", "ApplicationUser", user.Id);
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
        await _audit.LogForUserAsync(user.Id, "PasswordResetRequested", "ApplicationUser", user.Id);
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

        await _audit.LogForUserAsync(user.Id, "PasswordReset", "ApplicationUser", user.Id, "Reset with the emailed link");
        await SendChangedNoticeAsync(user);
        return result;
    }

    private Task SendChangedNoticeAsync(ApplicationUser user) =>
        _email.SendAsync(user.Email!, "Your password was changed",
            EmailTemplates.PasswordChanged(user.FullName, _email.AbsoluteUrl("/Account/Login")));
}
