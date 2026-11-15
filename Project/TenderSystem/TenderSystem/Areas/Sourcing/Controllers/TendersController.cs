using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class TendersController(ApplicationDbContext db, UserManager<ApplicationUser> users, AuditService audit, NotificationService notifications) : Controller
{
    public async Task<IActionResult> Index(string? searchTerm, string? status)
    {
        var query = db.Tenders.AsNoTracking();
        ViewData["Statuses"] = await query.Select(t => t.Status).Distinct().OrderBy(value => value).ToListAsync();
        searchTerm = searchTerm?.Trim();
        if (!string.IsNullOrEmpty(searchTerm))
            query = query.Where(t => t.TenderNumber.Contains(searchTerm) || t.Title.Contains(searchTerm)
                || t.Requisition.RequestNumber.Contains(searchTerm));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        ViewData["SearchTerm"] = searchTerm;
        ViewData["Status"] = status;
        return View(await query.Include(t => t.Requisition).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToListAsync());
    }

    public async Task<IActionResult> Create(int? requestId)
    {
        var model = new TenderSetup { RequestId = requestId ?? 0 };
        await LoadOptions(model);
        var request = await db.PurchaseRequisitions.AsNoTracking().Include(r => r.PurchaseRequisitionItems)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.Status == "Approved");
        if (request is not null)
        {
            model.Title = request.Title;
            model.Description = request.Reason + "\n\n" + string.Join("\n", request.PurchaseRequisitionItems.OrderBy(i => i.Id)
                .Select(i => $"{i.ItemName} — {i.Quantity:0.##} {i.Unit}: {i.Specifications}"));
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TenderSetup model, bool publishNow = false)
    {
        await LoadOptions(model);
        if (!model.Requests.Any(r => r.Value == model.RequestId.ToString()))
            ModelState.AddModelError(nameof(model.RequestId), "Select an approved purchase request.");
        if (model.Deadline is not DateTime deadline || deadline.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.Deadline), "Choose today or a later submission date.");
        if (model.Questions.Count != 6 || model.Questions.Sum(q => q.Weight) != 80 ||
            model.Questions.Any(q => decimal.Round(q.Weight, 2) != q.Weight))
            ModelState.AddModelError(nameof(model.Questions), "Enter six technical questions with weights totaling exactly 80%, with up to two decimal places.");
        if (!model.DepartmentReviewers.Any(account => account.Value == model.DepartmentReviewerId))
            ModelState.AddModelError(nameof(model.DepartmentReviewerId), "Select an eligible representative from the requesting department.");
        if (publishNow && !model.CommitteeReady)
            ModelState.AddModelError(string.Empty, "An administrator must assign all three fixed reviewers before publishing. You can save a draft now.");
        if (!ModelState.IsValid) return View(model);

        var officer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (officer is null || await users.FindByIdAsync(officer) is null) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var updated = await db.PurchaseRequisitions.Where(r => r.Id == model.RequestId && r.Status == "Approved")
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, "Converted to Tender"));
        if (updated == 0)
        {
            ModelState.AddModelError(nameof(model.RequestId), "This request is no longer available for conversion.");
            return View(model);
        }

        var budget = await db.PurchaseRequisitions.Where(r => r.Id == model.RequestId).Select(r => r.EstimatedBudget).SingleAsync();
        var tender = new Tender
        {
            TenderNumber = "TND-" + Guid.NewGuid().ToString("N")[..24],
            RequisitionId = model.RequestId,
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Terms = model.Terms.Trim(),
            SubmissionDeadline = model.Deadline!.Value.Date.AddDays(1).AddTicks(-1),
            EstimatedValue = budget ?? 0,
            CreatedByUserId = officer,
            EvaluationCriteria = model.Questions.Select(q => new EvaluationCriterion
            {
                Name = q.Name.Trim(), Type = "Technical", WeightPercent = q.Weight, MaxScore = 5
            }).ToList(),
            CommitteeMembers = [new CommitteeMember { MemberRole = TenderSetup.Seats[0].Code, UserId = model.DepartmentReviewerId! }]
        };
        tender.EvaluationCriteria.Add(new EvaluationCriterion
        {
            Name = "Eligible price ranking", Type = "Financial", WeightPercent = 20, MaxScore = 1
        });
        if (publishNow)
        {
            AssignFixedReviewers(tender, model.FixedReviewers);
            tender.Status = "Published";
            tender.PublishDate = DateTime.UtcNow;
            foreach (var criterion in tender.EvaluationCriteria) criterion.IsLocked = true;
        }
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        await audit.LogAsync("Created", "Tender", tender.Id.ToString());
        if (publishNow) await audit.LogAsync("Published", "Tender", tender.Id.ToString());
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = publishNow
            ? "Tender published. Choose suppliers, then review their submitted offers before sending to the committee."
            : "Draft saved. Publish when ready to share the brief and invite suppliers.";
        return RedirectToAction(nameof(Details), new { id = tender.Id, tab = publishNow ? null : "invitations" });
    }

    public async Task<IActionResult> Details(int id, string? category)
    {
        var tender = await db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.Requisition).ThenInclude(r => r.Department).Include(t => t.EvaluationCriteria)
            .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
            .Include(t => t.CommitteeMembers).ThenInclude(m => m.User)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();

        var criteria = tender.EvaluationCriteria.Where(c => c.Type == "Technical").ToArray();
        var reviewers = TenderSetup.Seats.Select(s => tender.CommitteeMembers
            .Where(m => m.MemberRole == s.Code).ToArray()).ToArray();
        var bids = tender.Bids.Where(b => b.Status is "Compliant" or "Accepted").ToArray();
        var distinctReviewers = reviewers.Where(r => r.Length == 1).Select(r => r[0].UserId).Distinct().Count();
        var comparable = bids.Length > 0 && bids.All(b => b.TotalPrice > 0 && !string.IsNullOrWhiteSpace(b.Currency))
            && bids.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && !tender.Bids.Any(b => b.Status is "Submitted" or "Under Review");
        var financialScores = new Dictionary<decimal, decimal>();
        if (comparable)
        {
            var prices = bids.Select(bid => bid.TotalPrice).Distinct().Order().ToArray();
            for (var rank = 0; rank < prices.Length; rank++)
                financialScores.Add(prices[rank], 20m * (prices.Length - rank) / prices.Length);
        }
        var validRubric = criteria.Sum(c => c.WeightPercent) == 80 && criteria.All(c => c.MaxScore > 0)
            && tender.EvaluationCriteria.Where(c => c.Type == "Financial").Sum(c => c.WeightPercent) == 20;

        ViewData["Scores"] = bids.Select(bid =>
        {
            var technical = new decimal?[4];
            for (var i = 0; i < technical.Length && validRubric && distinctReviewers == 4; i++)
            {
                decimal points = 0;
                var complete = true;
                foreach (var criterion in criteria)
                {
                    var score = bid.TechnicalEvaluations.Where(e => e.CriterionId == criterion.Id
                            && e.EvaluatorUserId == reviewers[i][0].UserId)
                        .OrderByDescending(e => e.EvaluatedAt).ThenByDescending(e => e.Id).FirstOrDefault();
                    if (score is null || !score.Score.HasValue || score.Score < 0 || score.Score > criterion.MaxScore)
                    {
                        complete = false;
                        break;
                    }
                    points += score.Score.Value / criterion.MaxScore * criterion.WeightPercent;
                }
                if (complete) technical[i] = points;
            }
            decimal? average = technical.All(s => s.HasValue) ? technical.Average() : null;
            decimal? financial = comparable ? financialScores[bid.TotalPrice] : null;
            return new TenderOfferScore(bid.SupplierProfile.CompanyName, bid.TotalPrice, bid.Currency,
                technical, average, financial, average + financial);
        }).ToList();

        var categories = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == "Approved" && s.Category.Trim() != "")
            .Select(s => s.Category.Trim()).Distinct().OrderBy(c => c).ToListAsync();
        ViewData["InvitationCategories"] = categories;
        if (category is not null && !categories.Contains(category)) category = null;
        ViewData["InvitationCategory"] = category;
        ViewData["InvitableSuppliers"] = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == "Approved" && (category == null || s.Category.Trim() == category) && !s.Invitations.Any(i => i.TenderId == id))
            .OrderBy(s => s.CompanyName).ThenBy(s => s.Id)
            .Select(s => new SelectListItem(s.CompanyName, s.Id.ToString())).ToListAsync();
        return View(tender);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tender = await db.Tenders.Include(t => t.EvaluationCriteria).Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        if (tender.Status != "Draft" || tender.SubmissionDeadline <= DateTime.UtcNow ||
            tender.EvaluationCriteria.Where(c => c.Type == "Technical").Sum(c => c.WeightPercent) != 80 ||
            tender.EvaluationCriteria.Where(c => c.Type == "Financial").Sum(c => c.WeightPercent) != 20 ||
            tender.EvaluationCriteria.Sum(c => c.WeightPercent) != 100)
        {
            TempData["TenderError"] = "Publish requires a draft with a future deadline and an 80/20 rubric.";
            return RedirectToAction(nameof(Details), new { id, tab = "invitations" });
        }

        var departmentMembers = tender.CommitteeMembers.Where(member => member.MemberRole == TenderSetup.Seats[0].Code).ToArray();
        var fixedReviewers = await LoadFixedReviewers();
        if (departmentMembers.Length != 1 || !await users.Users.AnyAsync(user =>
            user.Id == departmentMembers[0].UserId && user.IsActive) ||
            fixedReviewers.Any(reviewer => reviewer is null || reviewer.Id == departmentMembers[0].UserId))
        {
            TempData["TenderError"] = "Publishing requires an active department representative and one distinct, active admin assignment for each of the three fixed committee positions.";
            return RedirectToAction(nameof(Details), new { id, tab = "invitations" });
        }

        var changed = await db.Tenders.Where(t => t.Id == id && t.Status == "Draft")
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, "Published")
                .SetProperty(t => t.PublishDate, DateTime.UtcNow));
        if (changed == 0) return RedirectToAction(nameof(Details), new { id, tab = "invitations" });
        AssignFixedReviewers(tender, fixedReviewers);
        foreach (var criterion in tender.EvaluationCriteria) criterion.IsLocked = true;
        await db.SaveChangesAsync();
        await audit.LogAsync("Published", "Tender", tender.Id.ToString());
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = "Tender published. Choose suppliers, then review their submitted offers before sending to the committee.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToCommittee(int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var tender = await db.Tenders.Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        var destination = RedirectToAction(nameof(Details), new { id, tab = "criteria" });
        var members = tender.CommitteeMembers.Select(member => member.UserId).Distinct().ToArray();
        if (tender.PublishDate is null || tender.Status == "Draft" || members.Length != 4 ||
            await users.Users.CountAsync(user => members.Contains(user.Id) && user.IsActive) != 4)
        {
            TempData["TenderError"] = "Publish the tender and verify all four committee accounts are active before sending the brief.";
            return destination;
        }
        var offerStatuses = await db.Bids.Where(bid => bid.TenderId == id).Select(bid => bid.Status).ToListAsync();
        if (!offerStatuses.Any(status => status is Statuses.BidStatus.Compliant or Statuses.BidStatus.Accepted) ||
            offerStatuses.Any(status => status is Statuses.BidStatus.Submitted or Statuses.BidStatus.UnderReview))
        {
            TempData["TenderError"] = "Finish compliance review for all submitted offers. At least one offer must be compliant before sending to the committee.";
            return destination;
        }

        var link = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id = tender.Id })
            ?? throw new InvalidOperationException("Could not generate the evaluation brief link.");
        foreach (var member in tender.CommitteeMembers)
        {
            await notifications.NotifyAsync(member.UserId, "TenderEvaluation", "Evaluation brief shared",
                $"The evaluation brief for {tender.TenderNumber} — {tender.Title} is ready to review.", link);
        }
        await audit.LogAsync("Evaluation brief sent", "Tender", tender.Id.ToString());
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = "Evaluation brief sent to all four committee members.";
        return destination;
    }

    private async Task LoadOptions(TenderSetup model)
    {
        model.Requests = await db.PurchaseRequisitions.AsNoTracking().Where(r => r.Status == "Approved")
            .OrderBy(r => r.RequestNumber)
            .Select(r => new SelectListItem(r.RequestNumber + " · " + r.Title, r.Id.ToString())).ToListAsync();
        model.FixedReviewers = await LoadFixedReviewers();

        var departmentId = await db.PurchaseRequisitions.AsNoTracking()
            .Where(request => request.Id == model.RequestId && request.Status == "Approved")
            .Select(request => (int?)request.DepartmentId).FirstOrDefaultAsync();
        if (departmentId is null) return;

        var fixedIds = model.FixedReviewers.OfType<ApplicationUser>().Select(user => user.Id).ToArray();
        var departmentRoles = new[] { Roles.Requester, Roles.EvaluationCommittee, Roles.SourcingOfficer, Roles.SourcingManager };
        var accounts = await users.Users.AsNoTracking()
            .Where(user => user.IsActive && user.DepartmentId == departmentId && !fixedIds.Contains(user.Id))
            .ToListAsync();
        foreach (var account in accounts
            .OrderByDescending(user => string.Equals(user.JobTitle, "Department Manager", StringComparison.OrdinalIgnoreCase))
            .ThenBy(user => user.FullName))
        {
            var roles = (await users.GetRolesAsync(account)).Intersect(departmentRoles).ToArray();
            if (roles.Length == 0) continue;
            var position = string.IsNullOrWhiteSpace(account.JobTitle) ? string.Join(", ", roles) : account.JobTitle;
            model.DepartmentReviewers.Add(new SelectListItem($"{account.FullName} · {position}", account.Id));
        }
    }

    private async Task<ApplicationUser?[]> LoadFixedReviewers() =>
    [
        await FindFixedReviewer(TenderSetup.Seats[1].Title, Roles.EvaluationCommittee),
        await FindFixedReviewer(TenderSetup.Seats[2].Title, Roles.EvaluationCommittee),
        await FindFixedReviewer(TenderSetup.Seats[3].Title, Roles.SourcingManager, Roles.EvaluationCommittee)
    ];

    private static void AssignFixedReviewers(Tender tender, ApplicationUser?[] reviewers)
    {
        for (var i = 0; i < reviewers.Length; i++)
        {
            var role = TenderSetup.Seats[i + 1].Code;
            var member = tender.CommitteeMembers.SingleOrDefault(member => member.MemberRole == role);
            if (member is null)
                tender.CommitteeMembers.Add(new CommitteeMember { MemberRole = role, UserId = reviewers[i]!.Id });
            else
                member.UserId = reviewers[i]!.Id;
        }
    }

    private async Task<ApplicationUser?> FindFixedReviewer(string title, params string[] roles)
    {
        var accounts = new List<ApplicationUser>();
        foreach (var role in roles)
            accounts.AddRange(await users.GetUsersInRoleAsync(role));
        var matches = accounts.DistinctBy(account => account.Id)
            .Where(account => account.IsActive && string.Equals(account.JobTitle?.Trim(), title, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;

    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(int id, string? category, int[] supplierIds)
    {
        var destination = RedirectToAction(nameof(Details), new { id, tab = "invitations", category });
        if (!ModelState.IsValid || supplierIds.Length == 0)
        {
            TempData["InvitationError"] = "Select at least one approved supplier.";
            return destination;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var tender = await db.Tenders.Include(t => t.Invitations).Include(t => t.TenderSuppliers)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();

        var now = DateTime.UtcNow;
        if (tender.Status is not ("Published" or "Open") || tender.SubmissionDeadline <= now)
        {
            TempData["InvitationError"] = "Invitations can only be sent for published or open tenders before the deadline.";
            return destination;
        }

        var selected = supplierIds.Distinct().ToArray();
        if (!await CanInviteSuppliers(tender, category, selected))
        {
            TempData["InvitationError"] = "The selection changed. Choose approved suppliers who have not already been invited.";
            return destination;
        }

        AddInvitations(tender, selected, now);
        await db.SaveChangesAsync();
        await audit.LogAsync("Invitations sent", "Tender", id.ToString(), $"Suppliers invited: {selected.Length}");
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = $"{selected.Length} invitation(s) sent.";
        return destination;
    }

    private async Task<bool> CanInviteSuppliers(Tender tender, string? category, int[] selected)
    {
        if (tender.Invitations.Any(invitation => selected.Contains(invitation.SupplierProfileId))) return false;
        var approved = await db.SupplierProfiles.CountAsync(supplier => selected.Contains(supplier.Id)
            && supplier.Status == "Approved" && (category == null || supplier.Category.Trim() == category));
        return approved == selected.Length;
    }

    private static void AddInvitations(Tender tender, int[] selected, DateTime now)
    {
        foreach (var supplierId in selected)
        {
            tender.Invitations.Add(new Invitation
            {
                SupplierProfileId = supplierId, SentAt = now, ExpiresAt = tender.SubmissionDeadline
            });
            if (!tender.TenderSuppliers.Any(s => s.SupplierProfileId == supplierId))
            {
                tender.TenderSuppliers.Add(new TenderSupplier { SupplierProfileId = supplierId, AddedAt = now });
            }
        }
    }

    public IActionResult Suppliers() => RedirectToAction(nameof(Index));
}
