using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Services.Auth;

/// <summary>
/// The employee number is made by the system (EMP-0001, EMP-0002 ...). Nobody types it.
/// </summary>
public static class EmployeeNumbers
{
    private const string Prefix = "EMP-";

    /// <summary>
    /// Makes sure the employee has a profile with an employee number. Safe to call many times.
    /// A new profile keeps hire date, work phone and language empty, so onboarding still asks for them.
    /// </summary>
    public static async Task EnsureAsync(ApplicationDbContext db, string userId, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var profile = await db.EmployeeProfiles.SingleOrDefaultAsync(row => row.UserId == userId, ct);
            if (profile is not null && !string.IsNullOrWhiteSpace(profile.EmployeeNumber)) return;

            var number = await NextAsync(db, ct);
            if (profile is null)
            {
                profile = new EmployeeProfile { UserId = userId, EmployeeNumber = number, PreferredLanguage = string.Empty };
                db.EmployeeProfiles.Add(profile);
            }
            else
            {
                profile.EmployeeNumber = number;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException)
            {
                // Someone took the same number at the same moment: forget our change and try the next free one.
                db.Entry(profile).State = EntityState.Detached;
            }
        }
    }

    private static async Task<string> NextAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var numbers = await db.EmployeeProfiles.AsNoTracking()
            .Where(row => row.EmployeeNumber.StartsWith(Prefix))
            .Select(row => row.EmployeeNumber)
            .ToListAsync(ct);
        var highest = numbers
            .Select(value => int.TryParse(value.AsSpan(Prefix.Length), out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();
        return Prefix + (highest + 1).ToString("D4");
    }
}
