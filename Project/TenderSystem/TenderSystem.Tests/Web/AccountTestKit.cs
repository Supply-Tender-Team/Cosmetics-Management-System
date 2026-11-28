using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Controllers;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Auth;
using TenderSystem.Services.Email;
using TenderSystem.Services.Files;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Tests.Web;

/// <summary>
/// The real AccountController with the real ASP.NET Identity (same password and lockout rules as Program.cs) on an in-memory database.
/// Only what the password login does not use is left empty: the face, fingerprint, e-mail, notification and file services.
/// </summary>
internal sealed class AccountTestKit : IDisposable
{
    public const string Password = "Correct-Horse-1";

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public ApplicationDbContext Db { get; }
    public UserManager<ApplicationUser> Users { get; }
    public AccountController Controller { get; }
    public FakeFiles Files { get; } = new();
    public PasswordService Passwords { get; }
    public string MailFolder { get; } = Path.Combine(Path.GetTempPath(), "tender-tests-mail-" + Guid.NewGuid().ToString("N"));

    /// <param name="mailWorks">true: e-mails are saved as files in MailFolder (like Development). false: the mail cannot be sent (like a Production site without SMTP).</param>
    public AccountTestKit(bool mailWorks = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddControllersWithViews();                          // View(model) needs the view services
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 10;                       // the same rules as Program.cs
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.AddScoped<AuditService>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        var sp = _scope.ServiceProvider;

        Db = sp.GetRequiredService<ApplicationDbContext>();
        Users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var http = new DefaultHttpContext { RequestServices = sp };
        http.Request.Headers.UserAgent = "xunit";
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = http;

        var email = new EmailService(Options.Create(new EmailOptions()), new TestEnvironment(MailFolder, mailWorks), NullLogger<EmailService>.Instance);
        Passwords = new PasswordService(Users, sp.GetRequiredService<SignInManager<ApplicationUser>>(), email, sp.GetRequiredService<AuditService>(), NullLogger<PasswordService>.Instance);

        Controller = new AccountController(sp.GetRequiredService<SignInManager<ApplicationUser>>(), Users, Db, sp.GetRequiredService<AuditService>(),
            Passwords, null!, null!, new NotificationService(Db), Files, Options.Create(new UploadOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            Url = new LocalOnlyUrlHelper(),
        };

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All) roles.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
    }

    /// <summary>An account that exists with the known password. Employees are active by default; a supplier also gets a profile with the given status.</summary>
    public async Task<ApplicationUser> AddUserAsync(string email, string role, bool active = true, bool mustChangePassword = false, string? supplierStatus = null)
    {
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = "Test " + role, IsActive = active, MustChangePassword = mustChangePassword,
            UserType = role == Roles.Supplier ? "Supplier" : "Employee", LockoutEnabled = true,
        };
        var created = await Users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await Users.AddToRoleAsync(user, role);
        if (supplierStatus != null)
        {
            Db.SupplierProfiles.Add(new SupplierProfile { UserId = user.Id, CompanyName = "Co " + email, Status = supplierStatus });
            await Db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>The e-mails that were "sent" to this address, newest last (only when mailWorks is true).</summary>
    public List<string> MailsTo(string address)
    {
        var folder = Path.Combine(MailFolder, "App_Data", "emails");           // where EmailService saves its files in Development
        if (!Directory.Exists(folder)) return [];
        var safe = System.Text.RegularExpressions.Regex.Replace(address, @"[^a-zA-Z0-9]+", "_");
        return Directory.GetFiles(folder, "*_" + safe + ".html").OrderBy(f => f).Select(File.ReadAllText).ToList();
    }

    /// <summary>The (token) part of the reset link inside the last mail to this address, or null when there is no such mail.</summary>
    public string? ResetTokenFor(string address)
    {
        var mail = MailsTo(address).LastOrDefault(m => m.Contains("Reset your password"));
        var match = mail == null ? null : System.Text.RegularExpressions.Regex.Match(mail, @"token=([A-Za-z0-9_\-%]+)");
        return match is { Success: true } ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        if (Directory.Exists(MailFolder)) Directory.Delete(MailFolder, true);
    }

    internal sealed class TestEnvironment(string root, bool development) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = development ? Environments.Development : Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>A file store that keeps nothing. FailWith makes the next save fail, like a full disk or a refused file.</summary>
    public sealed class FakeFiles : IFileStorageService
    {
        public string? FailWith { get; set; }
        public List<string> Saved { get; } = [];

        public Task<StoredFile> SaveAsync(Stream content, string originalName, string contentType, CancellationToken cancellationToken = default)
        {
            if (FailWith != null) throw new InvalidOperationException(FailWith);
            Saved.Add(originalName);
            return Task.FromResult(new StoredFile(originalName, "stored-" + originalName, "uploads/stored-" + originalName, contentType, 3));
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    }

    /// <summary>Url.IsLocalUrl like the real one: only paths on this site.</summary>
    private sealed class LocalOnlyUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext => throw new NotSupportedException();
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
        public string? Action(UrlActionContext c) =>                // "/Account/ResetPassword?email=..&token=.."
            "/" + c.Controller + "/" + c.Action + "?" + string.Join("&", new RouteValueDictionary(c.Values).Select(v => v.Key + "=" + Uri.EscapeDataString(v.Value?.ToString() ?? "")));
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}
