using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Committee;

/// <summary>A person who can sit on an evaluation committee.</summary>
public sealed record EvaluatorOption(string Id, string FullName, string Email, string Department);

/// <summary>
/// The ONE place that answers "who may be on an evaluation committee" and "who holds the three fixed positions".
/// The Admin page, the tender form and Publish all use it, so they can never disagree.
/// </summary>
public static class CommitteeDirectory
{
    /// <summary>
    /// Active employee accounts whose ONLY role is Evaluation Committee.
    /// A Requester, Sourcing Officer, Sourcing Manager, Top Committee or Admin account (or any account with two roles) is never eligible.
    /// </summary>
    public static IQueryable<ApplicationUser> EligibleEvaluators(ApplicationDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Users.Where(user => user.UserType == Statuses.UserTypes.Employee
            && user.IsActive
            && (user.LockoutEnd == null || user.LockoutEnd <= now)
            && db.UserRoles.Any(userRole => userRole.UserId == user.Id
                && db.Roles.Any(role => role.Id == userRole.RoleId && role.Name == Roles.EvaluationCommittee))
            && !db.UserRoles.Any(userRole => userRole.UserId == user.Id
                && db.Roles.Any(role => role.Id == userRole.RoleId && role.Name != Roles.EvaluationCommittee)));
    }

    public static Task<bool> IsEligibleAsync(ApplicationDbContext db, string userId, CancellationToken ct = default) =>
        EligibleEvaluators(db).AnyAsync(user => user.Id == userId, ct);

    /// <summary>All eligible evaluators, by name, for the Admin boxes.</summary>
    public static async Task<List<EvaluatorOption>> ListEligibleAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var rows = await EligibleEvaluators(db).AsNoTracking()
            .OrderBy(user => user.FullName).ThenBy(user => user.Email)
            .Select(user => new { user.Id, user.FullName, user.Email, Department = user.Department != null ? user.Department.Name : "" })
            .ToListAsync(ct);
        return rows.Select(row => new EvaluatorOption(row.Id, row.FullName, row.Email ?? "", row.Department)).ToList();
    }

    /// <summary>
    /// The people in the three Admin boxes, in the order of <see cref="CommitteePositions.AdminCodes"/>.
    /// A box that is empty, or whose person is no longer an eligible evaluator, gives null.
    /// </summary>
    public static async Task<ApplicationUser?[]> FixedReviewersAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var rows = await db.StandingCommitteeMembers.AsNoTracking().ToListAsync(ct);
        var userIds = rows.Select(row => row.UserId).ToArray();
        var eligible = await EligibleEvaluators(db).Where(user => userIds.Contains(user.Id)).ToListAsync(ct);
        return CommitteePositions.AdminCodes
            .Select(code => rows.FirstOrDefault(row => row.PositionCode == code))
            .Select(row => row is null ? null : eligible.FirstOrDefault(user => user.Id == row.UserId))
            .ToArray();
    }
}
