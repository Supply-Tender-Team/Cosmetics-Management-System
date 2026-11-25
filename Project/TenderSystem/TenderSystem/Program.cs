using DotNetEnv;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Seed;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Email;

// ─────────────────────────────────────────────────────────────────────────────
// 1) Load the .env file (it sits next to this file; parent folders are searched too,
//    so it works from Visual Studio, `dotnet run` and `dotnet ef`).
//    .env is NOT committed. Everybody copies .env.example to .env.
// ─────────────────────────────────────────────────────────────────────────────
// NoClobber: a real environment variable (CI, your shell) wins over the file.
try
{
    Env.NoClobber().TraversePath().Load();
}
catch (Exception error)
{
    // A typo in .env used to stop the application with an unreadable parser error: now the message names the line.
    throw new InvalidOperationException(EnvFileCheck.Explain(error), error);
}
foreach (var duplicate in EnvFileCheck.DuplicateKeys())
    Console.WriteLine($"WARNING .env: {duplicate} appears more than once. Only the FIRST line is used, the others are ignored.");

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// 2) Database (SQL Server + EF Core).
//    Models live in Data/Entities, their mapping in Data/Configurations, and the
//    DbContext is split into partial files (Data/DbSets) so teammates never edit
//    the same file.
// ─────────────────────────────────────────────────────────────────────────────
var connectionString = Env.GetString("DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "DB_CONNECTION is missing. Copy .env.example to .env and set your SQL Server connection string.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ─────────────────────────────────────────────────────────────────────────────
// 3) ASP.NET Core Identity (users + roles). We build our own login, onboarding
//    and biometric pages, so the built-in Identity UI is not used.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Password-reset links work for 1 hour (Identity's default is 24 hours).
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    // Outside Development the sign-in cookie is sent over HTTPS only (Development keeps working on http://localhost and through tunnels).
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
});

// Outside Development the data protection keys (they protect cookies and reset links) are saved in App_Data/keys, so a restart does not sign everybody out.
if (!builder.Environment.IsDevelopment())
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));

// ─────────────────────────────────────────────────────────────────────────────
// 4) Keys and settings that come from .env (Ollama, WebAuthn, face, uploads, email)
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAppOptionsFromEnv();

// ─────────────────────────────────────────────────────────────────────────────
// 5) MVC + feature modules. Each teammate registers his own services in his own
//    class that implements IFeatureModule (see Infrastructure/IFeatureModule.cs).
//    They are found automatically, so nobody edits this file.
// ─────────────────────────────────────────────────────────────────────────────
// The camera script posts JSON, so the anti-forgery token travels in this header.
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "RequestVerificationToken";
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddControllersWithViews();
builder.Services.AddFeatureModules(builder.Configuration);

var app = builder.Build();

// ─────────────────────────────────────────────────────────────────────────────
// 6) Database setup at startup:
//    - Development: apply pending migrations automatically (no need to run `dotnet ef database update`).
//    - Always: make sure the 7 roles and the departments exist.
//    - Development only: create one test account per role (see Data/Seed/IdentitySeeder.cs and .env).
// ─────────────────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        if (app.Environment.IsDevelopment())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

        await IdentitySeeder.SeedAsync(scope.ServiceProvider, IdentitySeeder.SeedTestUsersEnabled(app.Environment.IsDevelopment()));
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider, app.Environment.IsDevelopment());
        await DemoDataSeeder.FixManagerSeatsAsync(scope.ServiceProvider, app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database setup failed. Check DB_CONNECTION in .env and that SQL Server is running.");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Behind a tunnel or proxy (cloudflared, ngrok) the browser talks HTTPS to the tunnel while the tunnel may talk
// plain HTTP to us. Trust the proxy's "X-Forwarded-Proto" so the app knows the real scheme and does not
// redirect to localhost. Only the protocol is read: X-Forwarded-For stays untouched (the tunnel guard uses it).
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto });

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Areas first: /Admin/Users, /Supplier/Offers ...
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Development only: GET /dev/public-url shows which address the app uses for emails and fingerprint login.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/public-url", (HttpContext http, IOptions<WebAuthnOptions> webAuthn, IOptions<EmailOptions> email) =>
    {
        if (!IsThisComputer(http))
            return Results.NotFound();

        return Results.Json(new
        {
            publicUrl = AppOptionsExtensions.PublicUrl,
            emailLinksUse = email.Value.BaseUrl,
            webAuthnRpId = webAuthn.Value.RelyingPartyId,
            webAuthnOrigins = webAuthn.Value.Origins,
            thisRequestSeenAs = $"{http.Request.Scheme}://{http.Request.Host}",
        });
    });
}

// Development only: GET /dev/send-test-email?to=you@example.com  checks your email settings.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/send-test-email", async (string to, EmailService email, HttpContext http) =>
    {
        // Never available through a public tunnel (cloudflared, ngrok, dev tunnels add these headers) or from another computer.
        if (!IsThisComputer(http))
            return Results.NotFound();

        var sent = await email.SendAsync(to, "Test email from Sourcing & Tender",
            EmailTemplates.Notification("It works", "If you can read this, the email service is configured correctly.", email.AbsoluteUrl("/")));
        return Results.Json(new { to, sent, mode = email.Mode });
    });
}

app.Run();

// The /dev endpoints answer only a request that comes from this computer and did not pass through a tunnel or proxy.
static bool IsThisComputer(HttpContext http) =>
    !http.Request.Headers.ContainsKey("CF-Connecting-IP") && !http.Request.Headers.ContainsKey("X-Forwarded-For")
    && (http.Connection.RemoteIpAddress is null || System.Net.IPAddress.IsLoopback(http.Connection.RemoteIpAddress));
