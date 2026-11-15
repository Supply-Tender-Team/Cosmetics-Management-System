using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;

namespace TenderSystem.Data.Seed;

/// <summary>An account shown in the Development-only quick-login panel.</summary>
public record DevAccount(string Label, string Email, string Password);

/// <summary>
/// Creates the 7 roles and the departments (always) and one test account per role (Development only).
/// Test accounts are controlled from .env: SEED_TEST_USERS, SEED_TEST_PASSWORD, SEED_ADMIN_EMAIL, SEED_ADMIN_PASSWORD.
/// </summary>
public static class IdentitySeeder
{
    private const string FallbackTestPassword = "Test#12345!"; // development only, override in .env

    private static readonly (string Name, string Code)[] Departments =
    [
        ("Production", "PRD"), ("Packaging", "PKG"), ("Research & Development", "RND"), ("Marketing", "MKT"),
        ("Quality Control", "QC"), ("Finance", "FIN"), ("IT", "IT"), ("Sourcing", "SRC"),
    ];

    // email, full name, role, department code, job title
    private static readonly (string Email, string Name, string Role, string Dept, string Title)[] TestUsers =
    [
        ("requester@lumiere.local", "Sara Requester", Roles.Requester, "PKG", "Packaging Planner"),
        ("officer@lumiere.local", "Omar Officer", Roles.SourcingOfficer, "SRC", "Sourcing Officer"),
        ("evaluator@lumiere.local", "Layla Evaluator", Roles.EvaluationCommittee, "QC", "Technical Evaluator"),
        ("demo-department-manager@lumiere.local", "Demo Department Manager", Roles.EvaluationCommittee, "PKG", "Department Manager"),
        ("demo-sourcing-representative@lumiere.local", "Demo Sourcing Representative", Roles.EvaluationCommittee, "SRC", "Sourcing Department Representative"),
        ("manager@lumiere.local", "Nour Manager", Roles.SourcingManager, "SRC", "Sourcing Manager"),
        ("top@lumiere.local", "Hassan Top-Committee", Roles.TopCommittee, "FIN", "Committee Member"),
    ];

    private const string SupplierEmail = "supplier@bottlepro.local";

    public static bool SeedTestUsersEnabled(bool isDevelopment) =>
        isDevelopment && Env.GetBool("SEED_TEST_USERS", true);

    private static string TestPassword => Env.GetString("SEED_TEST_PASSWORD", FallbackTestPassword);
    private static string AdminEmail => Env.GetString("SEED_ADMIN_EMAIL", "admin@lumiere.local");
    private static string AdminPassword => Env.GetString("SEED_ADMIN_PASSWORD", TestPassword);

    /// <summary>Accounts listed on the login page in Development so the team can sign in with one click.</summary>
    public static IReadOnlyList<DevAccount> GetDevAccounts()
    {
        var list = new List<DevAccount> { new("Admin", AdminEmail, AdminPassword) };
        list.AddRange(TestUsers.Select(u => new DevAccount(Roles.LabelFor(u.Role), u.Email, TestPassword)));
        list.Add(new DevAccount("Supplier", SupplierEmail, TestPassword));
        return list;
    }

    public static async Task SeedAsync(IServiceProvider services, bool seedTestUsers)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // 1) roles (all environments)
        foreach (var role in Roles.All)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        // 2) departments (all environments)
        foreach (var (name, code) in Departments)
            if (!await db.Departments.AnyAsync(d => d.Code == code))
                db.Departments.Add(new Department { Name = name, Code = code });
        await db.SaveChangesAsync();

        if (!seedTestUsers) return;

        var deptIds = await db.Departments.ToDictionaryAsync(d => d.Code, d => d.Id);

        // 3) admin + one account per role
        await CreateUserAsync(userManager, AdminEmail, "System Admin", Roles.Admin, deptIds["IT"], "Administrator", AdminPassword);
        foreach (var u in TestUsers)
            await CreateUserAsync(userManager, u.Email, u.Name, u.Role, deptIds[u.Dept], u.Title, TestPassword);

        // 4) one approved supplier with its company profile
        var supplier = await CreateUserAsync(userManager, SupplierEmail, "Ali BottlePro", Roles.Supplier, null, "Sales Manager",
            TestPassword, userType: "Supplier");
        if (supplier != null && !await db.SupplierProfiles.AnyAsync(p => p.UserId == supplier.Id))
        {
            db.SupplierProfiles.Add(new SupplierProfile
            {
                UserId = supplier.Id,
                CompanyName = "BottlePro Co.",
                TaxNumber = "TEST-TAX-0001",
                CommercialRegNumber = "TEST-CR-0001",
                Country = "Egypt",
                City = "Cairo",
                Address = "Industrial Zone, Cairo",
                ContactPerson = "Ali BottlePro",
                ContactPhone = "+20 100 000 0000",
                Category = "Packaging",
                Status = "Approved",
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser?> CreateUserAsync(UserManager<ApplicationUser> users, string email, string fullName,
        string role, int? departmentId, string jobTitle, string password, string userType = "Employee")
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing != null) return existing;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            UserType = userType,
            DepartmentId = departmentId,
            JobTitle = jobTitle,
            IsActive = true,
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await users.AddToRoleAsync(user, role);
        return user;
    }
}
