using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;
using TenderSystem.Models;

namespace TenderSystem.Tests.Web;

/// <summary>
/// What the email + password login must always do (AccountController.Login). It runs with the real ASP.NET Identity, so the lockout is the real one.
/// These tests are the safety net before the login code is made shorter.
/// </summary>
public class LoginTests
{
    private static LoginViewModel Form(string email, string password = AccountTestKit.Password) => new() { Email = email, Password = password };

    private static string? Error(AccountTestKit kit) =>
        kit.Controller.ModelState.TryGetValue(string.Empty, out var e) ? e.Errors.FirstOrDefault()?.ErrorMessage : null;

    private static bool SignedIn(AccountTestKit kit) => kit.Controller.HttpContext.Response.Headers.ContainsKey("Set-Cookie");

    // ---------------------------------------------------------------- success

    [Theory]
    [InlineData(Roles.Admin, "/Admin/Dashboard")]
    [InlineData(Roles.Requester, "/Requester/Dashboard")]
    [InlineData(Roles.SourcingOfficer, "/Sourcing/Dashboard")]
    [InlineData(Roles.EvaluationCommittee, "/Evaluation/Dashboard")]
    [InlineData(Roles.SourcingManager, "/Manager/Dashboard")]
    [InlineData(Roles.TopCommittee, "/Committee/Dashboard")]
    public async Task An_active_employee_lands_on_the_dashboard_of_their_role(string role, string expected)
    {
        using var kit = new AccountTestKit();
        var user = await kit.AddUserAsync("a@test.com", role);

        var result = await kit.Controller.Login(Form("a@test.com"));

        Assert.Equal(expected, Assert.IsType<RedirectResult>(result).Url);
        Assert.True(SignedIn(kit));
        Assert.NotNull((await kit.Users.FindByIdAsync(user.Id))!.LastLoginAt);
        Assert.Contains(await kit.Db.BiometricAuthLogs.ToListAsync(), l => l.UserId == user.Id && l.Success && l.Method == Statuses.AuthMethod.Password);
    }

    [Fact]
    public async Task An_approved_supplier_lands_on_the_supplier_dashboard()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("s@test.com", Roles.Supplier, supplierStatus: "Approved");

        var result = await kit.Controller.Login(Form("s@test.com"));

        Assert.Equal("/Supplier/Dashboard", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task A_new_employee_with_a_temporary_password_must_change_it_first()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("n@test.com", Roles.Requester, mustChangePassword: true);

        var result = await kit.Controller.Login(Form("n@test.com"), returnUrl: "/Requester/Requests");

        Assert.Equal("/Shared/Onboarding/ChangePassword", Assert.IsType<RedirectResult>(result).Url);   // even with a return address
    }

    [Fact]
    public async Task A_local_return_address_is_followed_and_an_outside_one_is_not()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("r@test.com", Roles.Requester);

        var local = await kit.Controller.Login(Form("r@test.com"), returnUrl: "/Requester/Requests");
        Assert.Equal("/Requester/Requests", Assert.IsType<LocalRedirectResult>(local).Url);

        kit.Controller.ModelState.Clear();
        var outside = await kit.Controller.Login(Form("r@test.com"), returnUrl: "https://evil.example/steal");
        Assert.Equal("/Requester/Dashboard", Assert.IsType<RedirectResult>(outside).Url);
    }

    // ---------------------------------------------------------------- failure: nobody can learn which accounts exist

    [Fact]
    public async Task An_unknown_email_and_a_wrong_password_give_the_same_message()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("known@test.com", Roles.Requester);

        await kit.Controller.Login(Form("nobody@test.com"));
        var unknown = Error(kit);
        kit.Controller.ModelState.Clear();
        await kit.Controller.Login(Form("known@test.com", "Wrong-Password-9"));
        var wrong = Error(kit);

        Assert.Equal("Invalid email or password.", unknown);
        Assert.Equal(unknown, wrong);
        Assert.False(SignedIn(kit));
    }

    [Fact]
    public async Task A_failed_login_is_written_to_the_log_with_its_reason_and_the_password_is_cleared()
    {
        using var kit = new AccountTestKit();
        var user = await kit.AddUserAsync("l@test.com", Roles.Requester);

        var result = await kit.Controller.Login(Form("l@test.com", "Wrong-Password-9"));
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("", ((LoginViewModel)view.Model!).Password);          // read now: a controller shares one ViewData between calls

        kit.Controller.ModelState.Clear();                                // a real request starts with a fresh ModelState
        await kit.Controller.Login(Form("nobody@test.com"));
        var logs = await kit.Db.BiometricAuthLogs.ToListAsync();
        Assert.Contains(logs, l => l.UserId == user.Id && !l.Success && l.FailureReason == "Wrong password");
        Assert.Contains(logs, l => l.UserId == null && !l.Success && l.FailureReason == "Unknown email");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("lock@test.com", Roles.Requester);

        for (var i = 0; i < 5; i++)
        {
            kit.Controller.ModelState.Clear();                            // a real request starts with a fresh ModelState
            await kit.Controller.Login(Form("lock@test.com", "Wrong-Password-9"));
        }
        kit.Controller.ModelState.Clear();
        var result = await kit.Controller.Login(Form("lock@test.com"));            // now the RIGHT password

        Assert.IsType<ViewResult>(result);
        Assert.Contains("locked for 15 minutes", Error(kit));
        Assert.False(SignedIn(kit));
    }

    // ---------------------------------------------------------------- the account must be allowed to sign in

    [Fact]
    public async Task A_disabled_employee_cannot_sign_in()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("off@test.com", Roles.Requester, active: false);

        var result = await kit.Controller.Login(Form("off@test.com"));

        Assert.IsType<ViewResult>(result);
        Assert.Contains("disabled", Error(kit));
        Assert.False(SignedIn(kit));
    }

    [Theory]
    [InlineData("Pending", "waiting for Admin approval")]
    [InlineData("Rejected", "was rejected")]
    [InlineData("Suspended", "is suspended")]
    public async Task A_supplier_that_is_not_approved_cannot_sign_in(string status, string expectedText)
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("s@test.com", Roles.Supplier, supplierStatus: status);

        var result = await kit.Controller.Login(Form("s@test.com"));

        Assert.IsType<ViewResult>(result);
        Assert.Contains(expectedText, Error(kit));
        Assert.False(SignedIn(kit));
    }

    [Fact]
    public async Task A_supplier_without_a_profile_cannot_sign_in()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("np@test.com", Roles.Supplier);          // no profile row at all

        var result = await kit.Controller.Login(Form("np@test.com"));

        Assert.IsType<ViewResult>(result);
        Assert.False(SignedIn(kit));
    }

    [Fact]
    public async Task A_wrong_password_for_a_disabled_account_does_not_reveal_that_it_is_disabled()
    {
        using var kit = new AccountTestKit();
        await kit.AddUserAsync("off@test.com", Roles.Requester, active: false);

        await kit.Controller.Login(Form("off@test.com", "Wrong-Password-9"));

        Assert.Equal("Invalid email or password.", Error(kit));      // the "disabled" message only comes after the right password
    }
}
