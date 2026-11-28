using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;
using TenderSystem.Models;

namespace TenderSystem.Tests.Web;

/// <summary>
/// The password rules (PasswordService) and the forgot / reset pages (AccountController.Password.cs). The e-mails are really "sent": they are
/// saved as files in a temporary folder, and the tests read them, so the reset link that a user would click is the one tested.
/// </summary>
public class PasswordTests
{
    private const string NewPassword = "Brand-New-Pass-7";

    // ---------------------------------------------------------------- the Admin creates an employee

    [Fact]
    public async Task A_new_employee_gets_a_temporary_password_by_email_and_must_change_it()
    {
        using var kit = new AccountTestKit();

        var (result, userId) = await kit.Passwords.CreateEmployeeAsync("Omar Officer", "omar@test.com", Roles.SourcingOfficer, null, "Buyer", null);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        var user = (await kit.Users.FindByIdAsync(userId!))!;
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsActive);
        Assert.Equal("OmarOfficer", user.UserName);                           // the full name without spaces
        Assert.True(await kit.Users.IsInRoleAsync(user, Roles.SourcingOfficer));
        Assert.Contains(await kit.Db.AuditLogs.ToListAsync(), a => a.Action == "Created" && a.EntityId == userId);

        // the password in the e-mail is the real one, and it was never written to the audit log
        var mail = Assert.Single(kit.MailsTo("omar@test.com"));
        var temporary = System.Text.RegularExpressions.Regex.Match(mail, "<code[^>]*>([^<]+)</code>").Groups[1].Value;
        Assert.Equal(14, temporary.Length);
        Assert.True(await kit.Users.CheckPasswordAsync(user, temporary));
        Assert.DoesNotContain(await kit.Db.AuditLogs.ToListAsync(), a => (a.NewValues ?? "").Contains(temporary) || (a.OldValues ?? "").Contains(temporary));
    }

    [Fact]
    public async Task Two_employees_with_the_same_name_get_different_user_names()
    {
        using var kit = new AccountTestKit();

        await kit.Passwords.CreateEmployeeAsync("Omar Officer", "one@test.com", Roles.Requester, null, null, null);
        var (_, second) = await kit.Passwords.CreateEmployeeAsync("Omar Officer", "two@test.com", Roles.Requester, null, null, null);

        Assert.Equal("OmarOfficer2", (await kit.Users.FindByIdAsync(second!))!.UserName);
    }

    [Fact]
    public async Task A_name_without_usable_letters_uses_the_start_of_the_email()
    {
        using var kit = new AccountTestKit();

        var (_, id) = await kit.Passwords.CreateEmployeeAsync("عمر", "omar.k@test.com", Roles.Requester, null, null, null);

        Assert.Equal("omar.k", (await kit.Users.FindByIdAsync(id!))!.UserName);
    }

    [Fact]
    public async Task An_email_that_is_already_used_is_refused_and_nothing_is_sent()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("taken@test.com", Roles.Requester);

        var (result, userId) = await kit.Passwords.CreateEmployeeAsync("Someone Else", "taken@test.com", Roles.Requester, null, null, null);

        Assert.False(result.Succeeded);
        Assert.Null(userId);
        Assert.Empty(kit.MailsTo("taken@test.com"));
    }

    [Fact]
    public async Task When_the_welcome_email_cannot_be_sent_the_account_is_not_kept()
    {
        using var kit = new AccountTestKit(mailWorks: false);

        var (result, userId) = await kit.Passwords.CreateEmployeeAsync("Omar Officer", "omar@test.com", Roles.SourcingOfficer, null, null, null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "EmailNotSent");
        Assert.Null(userId);
        Assert.Null(await kit.Users.FindByEmailAsync("omar@test.com"));       // nobody is left with an account and no password
    }

    // ---------------------------------------------------------------- change the password (signed in)

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_ends_the_first_sign_in_steps()
    {
        using var kit = new AccountTestKit();
        var user = await kit.AddUserAsync("c@test.com", Roles.Requester, mustChangePassword: true);

        var wrong = await kit.Passwords.ChangePasswordAsync(user.Id, "Not-The-Password-1", NewPassword);
        Assert.False(wrong.Succeeded);
        Assert.True((await kit.Users.FindByIdAsync(user.Id))!.MustChangePassword);

        var right = await kit.Passwords.ChangePasswordAsync(user.Id, AccountTestKit.Password, NewPassword);

        Assert.True(right.Succeeded, string.Join("; ", right.Errors.Select(e => e.Description)));
        var after = (await kit.Users.FindByIdAsync(user.Id))!;
        Assert.False(after.MustChangePassword);
        Assert.Equal(Statuses.OnboardingStatus.InProgress, after.OnboardingStatus);
        Assert.True(await kit.Users.CheckPasswordAsync(after, NewPassword));
        Assert.Single(kit.MailsTo("c@test.com"), m => m.Contains("Your password was changed"));
        Assert.Contains(await kit.Db.AuditLogs.ToListAsync(), a => a.Action == "PasswordChanged");
    }

    [Fact]
    public async Task A_new_password_that_breaks_the_rules_is_refused()
    {
        using var kit = new AccountTestKit();
        var user = await kit.AddUserAsync("c@test.com", Roles.Requester);

        var result = await kit.Passwords.ChangePasswordAsync(user.Id, AccountTestKit.Password, "short");

        Assert.False(result.Succeeded);
        Assert.True(await kit.Users.CheckPasswordAsync(user, AccountTestKit.Password));      // the old password still works
    }

    // ---------------------------------------------------------------- forgot password: nobody can learn which accounts exist

    [Fact]
    public async Task The_forgot_password_page_answers_the_same_for_a_known_and_an_unknown_address()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("known@test.com", Roles.Requester);

        var known = await kit.Controller.ForgotPassword(new ForgotPasswordViewModel { Email = "known@test.com" });
        var unknown = await kit.Controller.ForgotPassword(new ForgotPasswordViewModel { Email = "nobody@test.com" });

        Assert.Equal("ForgotPasswordSent", Assert.IsType<ViewResult>(known).ViewName);
        Assert.Equal("ForgotPasswordSent", Assert.IsType<ViewResult>(unknown).ViewName);
        Assert.Single(kit.MailsTo("known@test.com"));              // only the real account got a mail
        Assert.Empty(kit.MailsTo("nobody@test.com"));
    }

    // ---------------------------------------------------------------- reset with the emailed link

    [Fact]
    public async Task The_emailed_link_sets_a_new_password_unlocks_the_account_and_works_only_once()
    {
        using var kit = new AccountTestKit();
        var user = await kit.AddUserAsync("r@test.com", Roles.Requester, mustChangePassword: true);
        for (var i = 0; i < 5; i++) await kit.Users.AccessFailedAsync(user);                // the account is locked out
        Assert.True(await kit.Users.IsLockedOutAsync(user));

        await kit.Controller.ForgotPassword(new ForgotPasswordViewModel { Email = "r@test.com" });
        var token = kit.ResetTokenFor("r@test.com");
        Assert.False(string.IsNullOrEmpty(token));

        var first = await kit.Controller.ResetPassword(new ResetPasswordViewModel { Email = "r@test.com", Token = token!, Password = NewPassword, ConfirmPassword = NewPassword });

        Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(first).ActionName);
        var after = (await kit.Users.FindByIdAsync(user.Id))!;
        Assert.True(await kit.Users.CheckPasswordAsync(after, NewPassword));
        Assert.False(await kit.Users.IsLockedOutAsync(after));
        Assert.False(after.MustChangePassword);
        Assert.Contains(await kit.Db.AuditLogs.ToListAsync(), a => a.Action == "PasswordReset");

        kit.Controller.ModelState.Clear();
        var again = await kit.Controller.ResetPassword(new ResetPasswordViewModel { Email = "r@test.com", Token = token!, Password = "Another-Pass-88", ConfirmPassword = "Another-Pass-88" });
        Assert.IsType<ViewResult>(again);                                                      // the same link a second time
        Assert.Contains("invalid or has expired", kit.Controller.ModelState[string.Empty]!.Errors[0].ErrorMessage);
        Assert.True(await kit.Users.CheckPasswordAsync(after, NewPassword));
    }

    [Fact]
    public async Task A_wrong_token_and_an_unknown_email_give_the_same_message_and_change_nothing()
    {
        using var kit = new AccountTestKit();
        var user = await kit.AddUserAsync("r@test.com", Roles.Requester);

        var badToken = await kit.Passwords.ResetPasswordAsync("r@test.com", "not-a-token", NewPassword);
        var garbage = await kit.Passwords.ResetPasswordAsync("r@test.com", "%%%", NewPassword);
        var unknown = await kit.Passwords.ResetPasswordAsync("nobody@test.com", "not-a-token", NewPassword);

        foreach (var r in new[] { badToken, garbage, unknown })
        {
            Assert.False(r.Succeeded);
            Assert.Equal("This link is invalid or has expired. Please request a new one.", Assert.Single(r.Errors).Description);
        }
        Assert.True(await kit.Users.CheckPasswordAsync(user, AccountTestKit.Password));
    }

    [Fact]
    public async Task A_reset_link_for_one_account_does_not_work_for_another()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("a@test.com", Roles.Requester);
        var victim = await kit.AddUserAsync("b@test.com", Roles.Requester);
        await kit.Controller.ForgotPassword(new ForgotPasswordViewModel { Email = "a@test.com" });
        var tokenOfA = kit.ResetTokenFor("a@test.com")!;

        var result = await kit.Passwords.ResetPasswordAsync("b@test.com", tokenOfA, NewPassword);

        Assert.False(result.Succeeded);
        Assert.True(await kit.Users.CheckPasswordAsync(victim, AccountTestKit.Password));
    }
}
