using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;
using TenderSystem.Models;

namespace TenderSystem.Tests.Web;

/// <summary>What the supplier self-registration (AccountController.RegisterSupplier) must always do. A safety net before the code is moved.</summary>
public class SupplierRegistrationTests
{
    private static IFormFile File(string name) =>
        new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", name) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

    private static RegisterSupplierViewModel Valid(string email = "new@supplier.com", string tax = "TAX-1") => new()
    {
        CompanyName = "New Supplier Co", TaxNumber = tax, CommercialRegNumber = "CR-1", Category = RegisterSupplierViewModel.Categories[0],
        Country = "Jordan", City = "Amman", Address = "1 Main street", ContactPerson = "Sam", ContactPhone = "0791234567",
        Email = email, Password = "Correct-Horse-1", ConfirmPassword = "Correct-Horse-1", AcceptTerms = true,
        CommercialRegistrationFile = File("cr.pdf"), TaxCertificateFile = File("tax.pdf"),
    };

    private static bool HasError(AccountTestKit kit, string key) =>
        kit.Controller.ModelState.TryGetValue(key, out var e) && e.Errors.Count > 0;

    // ---------------------------------------------------------------- success

    [Fact]
    public async Task A_valid_registration_creates_an_inactive_supplier_account_with_a_pending_profile_and_its_documents()
    {
        using var kit = new AccountTestKit();
        var model = Valid();
        model.OtherDocumentFile = File("other.pdf");

        var result = await kit.Controller.RegisterSupplier(model);

        Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(result).ActionName);
        var user = await kit.Users.FindByEmailAsync("new@supplier.com");
        Assert.NotNull(user);
        Assert.False(user!.IsActive);
        Assert.Equal("Supplier", user.UserType);
        Assert.True(await kit.Users.IsInRoleAsync(user, Roles.Supplier));

        var profile = await kit.Db.SupplierProfiles.Include(p => p.SupplierDocuments).SingleAsync();
        Assert.Equal("Pending", profile.Status);
        Assert.Equal(user.Id, profile.UserId);
        Assert.Equal(3, profile.SupplierDocuments.Count);
        Assert.Contains(await kit.Db.AuditLogs.ToListAsync(), a => a.Action == "Registered");
    }

    [Fact]
    public async Task A_new_supplier_cannot_sign_in_until_the_admin_approves()
    {
        using var kit = new AccountTestKit();
        await kit.Controller.RegisterSupplier(Valid());
        kit.Controller.ModelState.Clear();

        var result = await kit.Controller.Login(new LoginViewModel { Email = "new@supplier.com", Password = AccountTestKit.Password });

        Assert.IsType<ViewResult>(result);
        Assert.Contains("waiting for Admin approval", kit.Controller.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task A_website_without_https_gets_it_added()
    {
        using var kit = new AccountTestKit();
        var model = Valid();
        model.Website = "www.example.com";

        await kit.Controller.RegisterSupplier(model);

        Assert.Equal("https://www.example.com", (await kit.Db.SupplierProfiles.SingleAsync()).Website);
    }

    // ---------------------------------------------------------------- refused, and nothing is created

    [Fact]
    public async Task The_terms_must_be_accepted()
    {
        using var kit = new AccountTestKit();
        var model = Valid();
        model.AcceptTerms = false;

        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(model));
        Assert.True(HasError(kit, nameof(model.AcceptTerms)));
        Assert.Null(await kit.Users.FindByEmailAsync("new@supplier.com"));
    }

    [Fact]
    public async Task A_second_company_with_the_same_tax_number_or_the_same_email_is_refused()
    {
        using var kit = new AccountTestKit();
        await kit.Controller.RegisterSupplier(Valid());

        kit.Controller.ModelState.Clear();
        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(Valid(email: "other@supplier.com")));        // same tax number
        Assert.True(HasError(kit, nameof(RegisterSupplierViewModel.TaxNumber)));

        kit.Controller.ModelState.Clear();
        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(Valid(tax: "TAX-2")));                       // same email
        Assert.True(HasError(kit, nameof(RegisterSupplierViewModel.Email)));
        Assert.Single(kit.Db.SupplierProfiles);
    }

    [Fact]
    public async Task The_two_required_documents_must_be_attached()
    {
        using var kit = new AccountTestKit();
        var model = Valid();
        model.CommercialRegistrationFile = null;
        model.TaxCertificateFile = null;

        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(model));
        Assert.True(HasError(kit, nameof(model.CommercialRegistrationFile)));
        Assert.True(HasError(kit, nameof(model.TaxCertificateFile)));
    }

    [Fact]
    public async Task A_file_type_that_is_not_allowed_and_an_expired_document_are_refused()
    {
        using var kit = new AccountTestKit();
        var model = Valid();
        model.CommercialRegistrationFile = File("virus.exe");
        model.TaxCertificateExpiry = DateTime.UtcNow.AddDays(-1);

        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(model));
        Assert.True(HasError(kit, nameof(model.CommercialRegistrationFile)));
        Assert.True(HasError(kit, nameof(model.TaxCertificateExpiry)));
        Assert.Empty(kit.Files.Saved);
    }

    [Fact]
    public async Task A_category_that_is_not_in_the_list_is_refused()
    {
        using var kit = new AccountTestKit();
        var model = Valid();
        model.Category = "Weapons";

        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(model));
        Assert.True(HasError(kit, nameof(model.Category)));
    }

    [Fact]
    public async Task When_a_file_cannot_be_stored_nobody_is_left_with_an_account_and_no_documents()
    {
        using var kit = new AccountTestKit();
        kit.Files.FailWith = "The file could not be saved.";

        Assert.IsType<ViewResult>(await kit.Controller.RegisterSupplier(Valid()));

        Assert.Null(await kit.Users.FindByEmailAsync("new@supplier.com"));
        Assert.Empty(kit.Db.SupplierProfiles);
        Assert.Contains("could not be saved", kit.Controller.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }
}
