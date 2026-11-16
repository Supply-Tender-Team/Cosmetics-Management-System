using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Committee;

namespace TenderSystem.Areas.Admin.Controllers;

/// <summary>
/// Admin &gt; Evaluation committee. The Admin puts one Evaluation Committee account in each of the three fixed positions
/// (Department Financial Officer, Sourcing Department Representative, Financial Manager). The fourth position, the Department Manager,
/// belongs to the request's department and is handled on the tender form.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class CommitteeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;

    public CommitteeController(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index() => View(await BuildPageAsync(null));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveCommitteeInput input)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // 1) what was posted: one user id (or nothing) for each of the three positions. Anything else posted is ignored.
        var chosen = new Dictionary<string, string?>();
        foreach (var code in CommitteePositions.AdminCodes)
        {
            input.Selected.TryGetValue(code, out var id);
            chosen[code] = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        // 2) validate on the server (the page is never trusted)
        var errors = new List<string>();
        var picked = chosen.Values.OfType<string>().ToList();
        if (picked.Distinct().Count() != picked.Count)
            errors.Add("The same person cannot hold two positions.");
        foreach (var id in picked.Distinct())
        {
            if (!await CommitteeDirectory.IsEligibleAsync(_db, id))
            {
                var name = await _db.Users.Where(user => user.Id == id).Select(user => user.FullName).FirstOrDefaultAsync();
                errors.Add($"{name ?? "This account"} is not an active Evaluation Committee account. Only an account with the Evaluation Committee role and no other role can be on the committee.");
                continue;
            }
            // A person who is already the Department Manager of a draft tender cannot also be a fixed member: that tender could not be published.
            var drafts = await _db.CommitteeMembers.AsNoTracking()
                .Where(member => member.UserId == id && member.MemberRole == Statuses.CommitteeRole.Department
                    && member.Tender.Status == Statuses.TenderStatus.Draft)
                .Select(member => member.Tender.TenderNumber).ToListAsync();
            if (drafts.Count > 0)
            {
                var name = await _db.Users.Where(user => user.Id == id).Select(user => user.FullName).FirstOrDefaultAsync();
                errors.Add($"{name} is the Department Manager of the draft tender {string.Join(", ", drafts)}. Choose someone else, or change that tender first.");
            }
        }
        if (errors.Count > 0)
        {
            var failed = await BuildPageAsync(chosen);
            failed.Problems = errors;
            return View("Index", failed);
        }

        // 3) save all three boxes together (so two people can be swapped in one step)
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var current = await _db.StandingCommitteeMembers.Include(row => row.User).ToListAsync();
            var before = current.ToDictionary(row => row.PositionCode, row => row.User.FullName);
            var after = new Dictionary<string, string>();
            foreach (var id in picked)
                after[chosen.First(pair => pair.Value == id).Key] = await _db.Users.Where(user => user.Id == id).Select(user => user.FullName).FirstAsync();

            _db.StandingCommitteeMembers.RemoveRange(current);
            await _db.SaveChangesAsync();
            foreach (var (code, id) in chosen)
            {
                if (id is null) continue;
                _db.StandingCommitteeMembers.Add(new StandingCommitteeMember
                {
                    PositionCode = code,
                    UserId = id,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = adminId
                });
            }
            await _db.SaveChangesAsync();

            foreach (var code in CommitteePositions.AdminCodes)
            {
                before.TryGetValue(code, out var was);
                after.TryGetValue(code, out var now);
                if (was == now) continue;
                await _audit.LogAsync("StandingCommitteeChanged", "StandingCommittee", code,
                    $"{CommitteePositions.TitleFor(code)}: {was ?? "not assigned"} -> {now ?? "not assigned"}");
            }
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            var failed = await BuildPageAsync(chosen);
            failed.Problems = ["The committee changed while you were editing it. Reload the page and try again."];
            return View("Index", failed);
        }

        TempData["CommitteeSuccess"] = "The evaluation committee was saved. New tenders take these people when they are saved or published.";
        return RedirectToAction(nameof(Index));
    }

    /// <param name="posted">The choices to show again after a failed save; null = what is saved now.</param>
    private async Task<CommitteePageViewModel> BuildPageAsync(Dictionary<string, string?>? posted)
    {
        var options = await CommitteeDirectory.ListEligibleAsync(_db);
        var saved = await _db.StandingCommitteeMembers.AsNoTracking().Include(row => row.User).ThenInclude(user => user.Department).ToListAsync();
        var eligibleIds = options.Select(option => option.Id).ToHashSet();

        var page = new CommitteePageViewModel { Options = options };
        foreach (var code in CommitteePositions.AdminCodes)
        {
            var row = saved.FirstOrDefault(item => item.PositionCode == code);
            var box = new CommitteeBoxViewModel { Code = code, Title = CommitteePositions.TitleFor(code) };
            if (row is not null)
            {
                box.CurrentName = row.User.FullName;
                box.CurrentDepartment = row.User.Department?.Name;
                if (eligibleIds.Contains(row.UserId)) box.SelectedUserId = row.UserId;
                else
                {
                    box.Warning = $"{row.User.FullName} is no longer an active Evaluation Committee account. Choose someone else.";
                    page.Problems.Add($"{box.Title}: {row.User.FullName} can no longer be used.");
                }
            }
            else
            {
                page.Problems.Add($"{box.Title}: nobody is assigned yet.");
            }
            if (posted is not null && posted.TryGetValue(code, out var postedId)) box.SelectedUserId = postedId;
            page.Boxes.Add(box);
        }

        // Which departments have an Evaluation Committee account left for the Department Manager position?
        var inBoxes = saved.Select(row => row.UserId).ToHashSet();
        var departments = await _db.Departments.AsNoTracking().Where(department => department.IsActive).OrderBy(department => department.Name).ToListAsync();
        foreach (var department in departments)
        {
            var people = options.Where(option => option.Department == department.Name && !inBoxes.Contains(option.Id)).Select(option => option.FullName).ToList();
            page.Departments.Add(new DepartmentCoverageViewModel
            {
                Name = department.Name,
                Available = people.Count,
                People = string.Join(", ", people)
            });
        }
        return page;
    }
}
