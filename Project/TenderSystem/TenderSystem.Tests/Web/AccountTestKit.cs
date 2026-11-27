using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenderSystem.Controllers;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Audit;
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

    public AccountTestKit()
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

        Controller = new AccountController(sp.GetRequiredService<SignInManager<ApplicationUser>>(), Users, Db, sp.GetRequiredService<AuditService>(),
            null!, null!, null!, new NotificationService(Db), Files, Options.Create(new UploadOptions()))
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

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
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
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}
