using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;

// ─────────────────────────────────────────────────────────────────────────────
// 1) Load the .env file (it sits next to this file; parent folders are searched too,
//    so it works from Visual Studio, `dotnet run` and `dotnet ef`).
//    .env is NOT committed. Everybody copies .env.example to .env.
// ─────────────────────────────────────────────────────────────────────────────
Env.TraversePath().Load();

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
    options.UseSqlServer(connectionString));
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

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.SlidingExpiration = true;
});

// ─────────────────────────────────────────────────────────────────────────────
// 4) Keys and settings that come from .env (Ollama, WebAuthn, face, uploads, email)
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAppOptionsFromEnv();

// ─────────────────────────────────────────────────────────────────────────────
// 5) MVC + feature modules. Each teammate registers his own services in his own
//    class that implements IFeatureModule (see Infrastructure/IFeatureModule.cs).
//    They are found automatically, so nobody edits this file.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddFeatureModules(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

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

app.Run();
