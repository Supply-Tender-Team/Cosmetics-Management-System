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
        return View(await query.Include(t => t.Requisition).LatestActivityFirst(db).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> ReTenderQueue() =>
        View(await EligibleForReTender().Include(t => t.Requisition)
            .LatestActivityFirst(db).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> ReTender(int id)
    {
        var source = await EligibleForReTender().Include(t => t.Requisition)
            .SingleOrDefaultAsync(t => t.Id == id);
        if (source is null) return NotFound();

        var target = source.Requisition.TargetDate.Date;
        var defaultDeadline = DateTime.Today.AddDays(7);
        var model = new ReTenderSetup
        {
            Source = source,
            Deadline = target < DateTime.Today ? null : target < defaultDeadline ? target : defaultDeadline
        };
        await LoadDepartmentReviewers(source.Requisition.DepartmentId, await LoadFixedReviewers(), model.DepartmentReviewers);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReTender(int id, DateTime? deadline, string? departmentReviewerId)
    {
        var officer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (officer is null || await users.FindByIdAsync(officer) is null) return Forbid();

        var requestId = await db.Tenders.Where(t => t.Id == id).Select(t => (int?)t.RequisitionId).SingleOrDefaultAsync();
        if (requestId is null) return NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Every retry for a requisition takes this lock before checking its latest cycle.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [PurchaseRequisitions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {requestId.Value}");

        var source = await EligibleForReTender().Include(t => t.Requisition).Include(t => t.EvaluationCriteria)
            .SingleOrDefaultAsync(t => t.Id == id);
        if (source is null)
        {
            TempData["TenderError"] = "Only the latest finally rejected tender for a request can be re-tendered.";
            return RedirectToAction(nameof(ReTenderQueue));
        }

        var model = new ReTenderSetup { Source = source, Deadline = deadline, DepartmentReviewerId = departmentReviewerId };
        await LoadDepartmentReviewers(source.Requisition.DepartmentId, await LoadFixedReviewers(), model.DepartmentReviewers);
        ValidateSubmissionDeadline(deadline, source.Requisition.TargetDate);
        if (!model.DepartmentReviewers.Any(option => option.Value == departmentReviewerId))
            ModelState.AddModelError(nameof(model.DepartmentReviewerId), "Select an eligible department representative.");
        if (!ModelState.IsValid) return View(model);

        var tender = new Tender
        {
            TenderNumber = "TND-" + Guid.NewGuid().ToString("N")[..24],
            RequisitionId = source.RequisitionId,
            Title = source.Title,
            Description = source.Description,
            Terms = source.Terms,
            Status = Statuses.TenderStatus.Draft,
            SubmissionDeadline = DateOnly.FromDateTime(deadline!.Value).ToDateTime(TimeOnly.MaxValue),
            EstimatedValue = source.EstimatedValue,
            RequiresTopCommittee = source.RequiresTopCommittee,
            CreatedByUserId = officer,
            EvaluationCriteria = source.EvaluationCriteria.Select(c => new EvaluationCriterion
            {
                Name = c.Name, Type = c.Type, WeightPercent = c.WeightPercent, MaxScore = c.MaxScore
            }).ToList(),
            CommitteeMembers = [new CommitteeMember { MemberRole = TenderSetup.Seats[0].Code, UserId = departmentReviewerId! }]
        };
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        await audit.LogAsync("ReTendered", "Tender", tender.Id.ToString(), $"Created from tender {source.TenderNumber} (ID {source.Id}).");
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = "A new draft was created for this request.";
        return RedirectToAction(nameof(Details), new { id = tender.Id });
    }

    private IQueryable<Tender> EligibleForReTender() =>
        db.Tenders.AsNoTracking().Where(t => t.Status == Statuses.TenderStatus.Rejected
            && !db.Tenders.Any(newer => newer.RequisitionId == t.RequisitionId && newer.Id > t.Id));

    public async Task<IActionResult> Create(int? requestId)
    {
        var model = new TenderSetup { RequestId = requestId ?? 0 };
        await LoadOptions(model);
        var request = await db.PurchaseRequisitions.AsNoTracking().Include(r => r.PurchaseRequisitionItems)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.Status == "Approved");
        if (request is not null)
        {
            model.Title = request.Title;
            model.Description = request.Reason;
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = 2200)]
    public Task<IActionResult> Create(TenderSetup model) => SaveNewTenderAsync(model, publishNow: false);

    [HttpPost, ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = 2200)]
    public Task<IActionResult> PublishNew(TenderSetup model) => SaveNewTenderAsync(model, publishNow: true);

    private async Task<IActionResult> SaveNewTenderAsync(TenderSetup model, bool publishNow)
    {
        await LoadOptions(model);
        if (!model.Requests.Any(r => r.Value == model.RequestId.ToString()))
            ModelState.AddModelError(nameof(model.RequestId), "Select an approved purchase request.");
        ValidateSubmissionDeadline(model.Deadline, model.RequestTargetDate);
        if (model.Questions.Count == 0 || model.Questions.Sum(q => q.Weight) > 80 ||
            model.Questions.Any(q => decimal.Round(q.Weight, 2) != q.Weight))
            ModelState.AddModelError(nameof(model.Questions), "Add at least one technical question. Total weight cannot exceed 80%, with up to two decimal places.");
        if (!model.DepartmentReviewers.Any(account => account.Value == model.DepartmentReviewerId))
            ModelState.AddModelError(nameof(model.DepartmentReviewerId), "Select an eligible representative from the requesting department.");
        if (publishNow && !model.CommitteeReady)
            ModelState.AddModelError(string.Empty, CommitteeAssignmentMessage(model.FixedReviewers));
        if (!ModelState.IsValid) return View(nameof(Create), model);

        var officer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (officer is null || await users.FindByIdAsync(officer) is null) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var updated = await db.PurchaseRequisitions.Where(r => r.Id == model.RequestId && r.Status == "Approved")
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, "Converted to Tender"));
        if (updated == 0)
        {
            ModelState.AddModelError(nameof(model.RequestId), "This request is no longer available for conversion.");
            return View(nameof(Create), model);
        }

        var budget = await db.PurchaseRequisitions.Where(r => r.Id == model.RequestId).Select(r => r.EstimatedBudget).SingleAsync();
        var tender = new Tender
        {
            TenderNumber = "TND-" + Guid.NewGuid().ToString("N")[..24],
            Status = Statuses.TenderStatus.Draft,
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
        AssignFixedReviewers(tender, model.FixedReviewers);
        if (publishNow)
        {
            tender.Status = Statuses.TenderStatus.Published;
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

    private void ValidateSubmissionDeadline(DateTime? deadline, DateTime? requestTargetDate)
    {
        if (deadline is null || deadline.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(TenderSetup.Deadline), "Choose today or a later submission date.");
        else if (requestTargetDate is DateTime targetDate && deadline.Value.Date > targetDate.Date)
            ModelState.AddModelError(nameof(TenderSetup.Deadline),
                $"The submission deadline must be on or before the request's needed-by date ({targetDate:dd MMM yyyy}).");
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

        ViewData["CanReTender"] = tender.Status == Statuses.TenderStatus.Rejected
            && await EligibleForReTender().AnyAsync(t => t.Id == id);

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
        var validRubric = criteria.Sum(c => c.WeightPercent) is > 0 and <= 80 && criteria.All(c => c.MaxScore > 0)
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
        var tender = await db.Tenders.Include(t => t.Requisition).Include(t => t.EvaluationCriteria).Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        if (tender.SubmissionDeadline.Date > tender.Requisition.TargetDate.Date)
        {
            TempData["TenderError"] = $"The submission deadline exceeds the request's needed-by date ({tender.Requisition.TargetDate:dd MMM yyyy}).";
            return RedirectToAction(nameof(Details), new { id, tab = "invitations" });
        }

        if (tender.Status != "Draft" || tender.SubmissionDeadline <= DateTime.UtcNow ||
            tender.EvaluationCriteria.Where(c => c.Type == "Technical").Sum(c => c.WeightPercent) is <= 0 or > 80 ||
            tender.EvaluationCriteria.Where(c => c.Type == "Financial").Sum(c => c.WeightPercent) != 20)
        {
            TempData["TenderError"] = "Publish requires a draft with a future deadline, technical weights up to 80%, and a financial weight of 20%.";
            return RedirectToAction(nameof(Details), new { id, tab = "invitations" });
        }

        var departmentMembers = tender.CommitteeMembers.Where(member => member.MemberRole == TenderSetup.Seats[0].Code).ToArray();
        var fixedReviewers = await LoadFixedReviewers();
        if (fixedReviewers.Any(reviewer => reviewer is null))
        {
            TempData["TenderError"] = CommitteeAssignmentMessage(fixedReviewers);
            return RedirectToAction(nameof(Details), new { id, tab = "invitations" });
        }

        var departmentReviewers = new List<SelectListItem>();
        await LoadDepartmentReviewers(tender.Requisition.DepartmentId, fixedReviewers, departmentReviewers);
        if (departmentMembers.Length != 1 || !departmentReviewers.Any(option => option.Value == departmentMembers[0].UserId))
        {
            TempData["TenderError"] = "The department representative must be an active Evaluation Committee member from the requesting department.";
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

        var request = await db.PurchaseRequisitions.AsNoTracking()
            .Where(row => row.Id == model.RequestId && row.Status == Statuses.RequestStatus.Approved)
            .Select(row => new { row.DepartmentId, row.TargetDate }).SingleOrDefaultAsync();
        if (request is null) return;
        model.RequestTargetDate = request.TargetDate;
        model.RequestItems = await db.PurchaseRequisitionItems.AsNoTracking()
            .Where(item => item.RequisitionId == model.RequestId).OrderBy(item => item.Id).ToListAsync();

        await LoadDepartmentReviewers(request.DepartmentId, model.FixedReviewers, model.DepartmentReviewers);
    }

    private async Task LoadDepartmentReviewers(int departmentId, IEnumerable<ApplicationUser?> fixedReviewers,
        List<SelectListItem> options)
    {
        var fixedIds = fixedReviewers.OfType<ApplicationUser>().Select(user => user.Id).ToArray();
        var accounts = (await users.GetUsersInRoleAsync(Roles.EvaluationCommittee))
            .Where(user => user.IsActive && user.DepartmentId == departmentId && !fixedIds.Contains(user.Id));
        foreach (var account in accounts
            .OrderByDescending(user => string.Equals(user.JobTitle, "Department Manager", StringComparison.OrdinalIgnoreCase))
            .ThenBy(user => user.FullName))
        {
            var position = string.IsNullOrWhiteSpace(account.JobTitle) ? "Evaluation Committee" : account.JobTitle;
            options.Add(new SelectListItem($"{account.FullName} · {position}", account.Id));
        }
    }

    private async Task<ApplicationUser?[]> LoadFixedReviewers() =>
    [
        await FindFixedReviewer([TenderSetup.Seats[1].Title, "Financial Representative"], Roles.EvaluationCommittee),
        await FindFixedReviewer([TenderSetup.Seats[2].Title, "Technical Representative"], Roles.EvaluationCommittee),
        await FindFixedReviewer([TenderSetup.Seats[3].Title], Roles.SourcingManager, Roles.EvaluationCommittee)
    ];

    private static string CommitteeAssignmentMessage(ApplicationUser?[] reviewers)
    {
        var unresolved = TenderSetup.Seats.Skip(1)
            .Where((_, index) => reviewers[index] is null)
            .Select(seat => seat.Title).ToArray();
        return unresolved.Length == 0
            ? "Select an eligible department representative before publishing."
            : $"Publishing needs exactly one active assignment for each of these seats: {string.Join(", ", unresolved)}. You can still save a draft.";
    }

    private static void AssignFixedReviewers(Tender tender, ApplicationUser?[] reviewers)
    {
        for (var i = 0; i < reviewers.Length; i++)
        {
            if (reviewers[i] is not { } reviewer) continue;
            var role = TenderSetup.Seats[i + 1].Code;
            var member = tender.CommitteeMembers.SingleOrDefault(member => member.MemberRole == role);
            if (member is null)
                tender.CommitteeMembers.Add(new CommitteeMember { MemberRole = role, UserId = reviewer.Id });
            else
                member.UserId = reviewer.Id;
        }
    }

    private async Task<ApplicationUser?> FindFixedReviewer(string[] titles, params string[] roles)
    {
        var accounts = new List<ApplicationUser>();
        foreach (var role in roles)
            accounts.AddRange(await users.GetUsersInRoleAsync(role));
        var matches = accounts.DistinctBy(account => account.Id)
            .Where(account => account.IsActive && titles.Contains(account.JobTitle?.Trim(), StringComparer.OrdinalIgnoreCase))
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
