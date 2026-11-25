using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Committee;
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
            TempData["TenderError"] = "Only the latest tender for a request can be re-tendered when its status is Retender Required.";
            return RedirectToAction(nameof(ReTenderQueue));
        }

        var model = new ReTenderSetup { Source = source, Deadline = deadline, DepartmentReviewerId = departmentReviewerId };
        await LoadDepartmentReviewers(source.Requisition.DepartmentId, await LoadFixedReviewers(), model.DepartmentReviewers);
        ValidateSubmissionDeadline(deadline, source.Requisition.TargetDate, source.Requisition.SubmittedAt ?? source.Requisition.CreatedAt);
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
        // the random number above only had to be unique for the first save: now the tender has its id, so it gets a short readable number
        tender.TenderNumber = $"TND-{tender.CreatedAt:yyyy}-{tender.Id:0000}";
        await db.SaveChangesAsync();
        await audit.LogAsync("ReTendered", "Tender", tender.Id.ToString(), $"Created from tender {source.TenderNumber} (ID {source.Id}).");
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = "A new draft was created for this request.";
        return RedirectToAction(nameof(Details), new { id = tender.Id });
    }

    private IQueryable<Tender> EligibleForReTender() =>
        db.Tenders.AsNoTracking().Where(t => t.Status == Statuses.TenderStatus.RetenderRequired
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
        ValidateSubmissionDeadline(model.Deadline, model.RequestTargetDate, model.RequestSubmittedAt);
        if (!model.HasValidAllocation)
            ModelState.AddModelError(string.Empty, "Return this request for correction: technical and financial weights must total 100%.");
        if (model.TechnicalWeight == 0 && model.Questions.Count > 0)
            ModelState.AddModelError(nameof(model.Questions), "Financial-only tenders do not accept technical questions.");
        if (model.TechnicalWeight > 0 && (model.Questions.Count == 0 || model.Questions.Sum(q => q.Weight) != model.TechnicalWeight ||
            model.Questions.Any(q => decimal.Round(q.Weight, 2) != q.Weight)))
            ModelState.AddModelError(nameof(model.Questions), $"Distribute exactly {model.TechnicalWeight:0.##}% across the technical questions.");
        model.SupplierIds = model.SupplierIds.Distinct().ToArray();
        if ((publishNow && model.SupplierIds.Length == 0) || model.SupplierIds.Any(id =>
                !model.Suppliers.Any(s => s.Value == id.ToString() && (string.IsNullOrEmpty(model.SupplierCategory) || s.Group!.Name == model.SupplierCategory))))
            ModelState.AddModelError(nameof(model.SupplierIds), "Choose at least one approved supplier in the selected category.");
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
            RequiresTopCommittee = model.RequiresTopCommittee,
            CreatedByUserId = officer,
            EvaluationCriteria = model.Questions.Select(q => new EvaluationCriterion
            {
                Name = q.Name.Trim(), Type = "Technical", WeightPercent = q.Weight, MaxScore = 5
            }).ToList(),
            CommitteeMembers = [new CommitteeMember { MemberRole = TenderSetup.Seats[0].Code, UserId = model.DepartmentReviewerId! }]
        };
        // The committee enters one overall financial score through its existing rubric.
        tender.EvaluationCriteria.Add(new EvaluationCriterion
        {
            Name = TenderSetup.FinancialAssessment,
            Type = Statuses.CriterionType.Financial,
            WeightPercent = model.FinancialWeight,
            MaxScore = 5
        });
        AssignFixedReviewers(tender, model.FixedReviewers);
        if (!publishNow)
            foreach (var supplierId in model.SupplierIds)
                tender.TenderSuppliers.Add(new TenderSupplier { SupplierProfileId = supplierId, AddedAt = DateTime.UtcNow });
        if (publishNow)
        {
            tender.Status = Statuses.TenderStatus.Published;
            tender.PublishDate = DateTime.UtcNow;
            foreach (var criterion in tender.EvaluationCriteria) criterion.IsLocked = true;
            AddInvitations(tender, model.SupplierIds, DateTime.UtcNow);
        }
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        // the random number above only had to be unique for the first save: now the tender has its id, so it gets a short readable number
        tender.TenderNumber = $"TND-{tender.CreatedAt:yyyy}-{tender.Id:0000}";
        await db.SaveChangesAsync();
        await audit.LogAsync("Created", "Tender", tender.Id.ToString());
        if (publishNow)
        {
            await audit.LogAsync("Published", "Tender", tender.Id.ToString());
            await NotifyInvitedSuppliers(tender.Id, model.SupplierIds);
            await NotifyCommittee(tender.Id);
        }
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = publishNow
            ? "Tender published and invitations sent to the selected suppliers."
            : "Draft saved with your evaluation criteria and supplier selection. Publish from Overview when ready.";
        return RedirectToAction(nameof(Details), new { id = tender.Id });
    }

    private void ValidateSubmissionDeadline(DateTime? deadline, DateTime? requestTargetDate, DateTime? requestSubmittedAt = null)
    {
        if (deadline is null || deadline.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(TenderSetup.Deadline), "Choose today or a later submission date.");
        else if (requestSubmittedAt is DateTime sentAt && deadline.Value.Date < sentAt.Date)
            ModelState.AddModelError(nameof(TenderSetup.Deadline), "The submission deadline cannot be before the request was sent.");
        else if (requestTargetDate is DateTime targetDate && deadline.Value.Date > targetDate.Date)
            ModelState.AddModelError(nameof(TenderSetup.Deadline),
                $"The submission deadline must be on or before the request's needed-by date ({targetDate:dd MMM yyyy}).");
    }

    public async Task<IActionResult> Details(int id, string? category)
    {
        var tender = await db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.Requisition).ThenInclude(r => r.Department).Include(t => t.EvaluationCriteria)
            .Include(t => t.TenderSuppliers).ThenInclude(s => s.SupplierProfile)
            .Include(t => t.Invitations).ThenInclude(i => i.SupplierProfile)
            .Include(t => t.CommitteeMembers).ThenInclude(m => m.User)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();

        ViewData["CanReTender"] = await EligibleForReTender().AnyAsync(t => t.Id == id);

        var technicalCriteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).ToArray();
        var financialCriteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).ToArray();
        var reviewers = TenderSetup.Seats.Select(s => tender.CommitteeMembers
            .Where(m => m.MemberRole == s.Code).ToArray()).ToArray();
        var bids = tender.Bids.Where(b => b.Status is Statuses.BidStatus.Compliant or Statuses.BidStatus.Accepted).ToArray();
        var distinctReviewers = reviewers.Where(r => r.Length == 1).Select(r => r[0].UserId).Distinct().Count();
        var canScore = tender.CommitteeMembers.Count == reviewers.Length && distinctReviewers == reviewers.Length
            && EvaluationScoring.ValidRubric(tender.EvaluationCriteria);
        ViewData["CanRankOffers"] = bids.Length > 0
            && bids.All(b => b.SubmittedAt.HasValue && b.TotalPrice > 0 && !string.IsNullOrWhiteSpace(b.Currency))
            && bids.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && !tender.Bids.Any(b => b.Status is Statuses.BidStatus.Submitted or Statuses.BidStatus.UnderReview);

        ViewData["Scores"] = bids.Select(bid =>
        {
            var technical = new decimal?[reviewers.Length];
            var financial = new decimal?[reviewers.Length];
            for (var i = 0; i < reviewers.Length && canScore; i++)
            {
                var reviewerId = reviewers[i][0].UserId;
                technical[i] = EvaluationScoring.Points(technicalCriteria, bid.TechnicalEvaluations, reviewerId);
                var points = EvaluationScoring.Points(financialCriteria, bid.TechnicalEvaluations, reviewerId);
                if (points.HasValue && bid.FinancialEvaluations.Any(e => e.EvaluatorUserId == reviewerId
                    && e.ReadyAt.HasValue && e.TotalCost == bid.TotalPrice && e.Score == decimal.Round(points.Value, 2)))
                    financial[i] = points;
            }
            decimal? average = technical.All(s => s.HasValue) ? technical.Average() : null;
            decimal? financialAverage = financial.All(s => s.HasValue) ? financial.Average() : null;
            return new TenderOfferScore(bid.SupplierProfile.CompanyName, bid.TotalPrice, bid.Currency,
                technical, average, financialAverage, average + financialAverage);
        }).ToList();

        var categories = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == "Approved" && s.Category.Trim() != "")
            .Select(s => s.Category.Trim()).Distinct().OrderBy(c => c).ToListAsync();
        ViewData["InvitationCategories"] = categories;
        if (category is not null && !categories.Contains(category)) category = null;
        ViewData["InvitationCategory"] = category;
        ViewData["InvitableSuppliers"] = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == "Approved" && (category == null || s.Category.Trim() == category) && s.User.IsActive && !s.Invitations.Any(i => i.TenderId == id))
            .OrderBy(s => s.CompanyName).ThenBy(s => s.Id)
            .Select(s => new SelectListItem(s.CompanyName, s.Id.ToString())).ToListAsync();
        return View(tender);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id, int[] supplierIds, string? category = null, DateTime? deadline = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tender = await db.Tenders.Include(t => t.Requisition).Include(t => t.Invitations).Include(t => t.TenderSuppliers).Include(t => t.EvaluationCriteria).Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["TenderError"] = "Choose a valid submission deadline.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var requestedDeadline = deadline ?? tender.SubmissionDeadline;
        ValidateSubmissionDeadline(requestedDeadline, tender.Requisition.TargetDate,
            tender.Requisition.SubmittedAt ?? tender.Requisition.CreatedAt);
        if (!ModelState.IsValid)
        {
            TempData["TenderError"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id });
        }
        tender.SubmissionDeadline = requestedDeadline.Date.AddDays(1).AddTicks(-1);
        if (tender.SubmissionDeadline.Date > tender.Requisition.TargetDate.Date)
        {
            TempData["TenderError"] = $"The submission deadline exceeds the request's needed-by date ({tender.Requisition.TargetDate:dd MMM yyyy}).";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (tender.Status != "Draft" || tender.SubmissionDeadline <= DateTime.UtcNow ||
            tender.EvaluationCriteria.Any(c => c.Type == Statuses.CriterionType.Technical && c.WeightPercent <= 0) ||
            tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent) <= 0 ||
            tender.EvaluationCriteria.Sum(c => c.WeightPercent) != 100)
        {
            TempData["TenderError"] = "Publish requires a draft with a future deadline and technical and financial weights totaling 100%.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var departmentMembers = tender.CommitteeMembers.Where(member => member.MemberRole == TenderSetup.Seats[0].Code).ToArray();
        var fixedReviewers = await LoadFixedReviewers();
        if (fixedReviewers.Any(reviewer => reviewer is null))
        {
            TempData["TenderError"] = CommitteeAssignmentMessage(fixedReviewers);
            return RedirectToAction(nameof(Details), new { id });
        }

        var departmentReviewers = new List<SelectListItem>();
        await LoadDepartmentReviewers(tender.Requisition.DepartmentId, fixedReviewers, departmentReviewers);
        if (departmentMembers.Length != 1 || !departmentReviewers.Any(option => option.Value == departmentMembers[0].UserId))
        {
            TempData["TenderError"] = "The department representative must be an active Evaluation Committee member from the requesting department.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var selected = supplierIds.Distinct().ToArray();
        if (selected.Length == 0 || !await CanInviteSuppliers(tender, category, selected))
        {
            TempData["TenderError"] = "Select approved suppliers before publishing and sending invitations.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var changed = await db.Tenders.Where(t => t.Id == id && t.Status == "Draft")
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, "Published")
                .SetProperty(t => t.PublishDate, DateTime.UtcNow));
        if (changed == 0) return RedirectToAction(nameof(Details), new { id });
        AssignFixedReviewers(tender, fixedReviewers);
        foreach (var supplier in tender.TenderSuppliers.Where(s => !selected.Contains(s.SupplierProfileId)).ToList())
            tender.TenderSuppliers.Remove(supplier);
        AddInvitations(tender, selected, DateTime.UtcNow);
        foreach (var criterion in tender.EvaluationCriteria) criterion.IsLocked = true;
        await db.SaveChangesAsync();
        await audit.LogAsync("Published", "Tender", tender.Id.ToString());
        await NotifyInvitedSuppliers(id, selected);
        await NotifyCommittee(id);
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = "Tender published, supplier invitations sent and committee brief shared.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // The officer can change "needs the Top Committee" until the report reaches the Manager (later it has no effect).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTopCommittee(int id, bool required)
    {
        var allowed = new[] { Statuses.TenderStatus.Draft, Statuses.TenderStatus.Published, Statuses.TenderStatus.Open,
            Statuses.TenderStatus.Closed, Statuses.TenderStatus.UnderEvaluation };
        var changed = await db.Tenders.Where(t => t.Id == id && allowed.Contains(t.Status))
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RequiresTopCommittee, required));
        if (changed == 0)
            TempData["TenderError"] = "The final approval route can only be changed before the report goes to the Manager.";
        else
        {
            await audit.LogAsync(required ? "TopCommitteeRequired" : "TopCommitteeNotRequired", "Tender", id.ToString());
            TempData["InvitationSuccess"] = required
                ? "After the Manager approves, this tender goes to the Top Committee."
                : "The Manager's approval will award this tender directly.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task NotifyCommittee(int tenderId)
    {
        var tender = await db.Tenders.Include(t => t.CommitteeMembers).SingleAsync(t => t.Id == tenderId);
        var link = $"/Sourcing/CommitteeBrief/Index/{tenderId}";
        foreach (var member in tender.CommitteeMembers)
        {
            var sent = await db.Notifications.AnyAsync(n => n.UserId == member.UserId
                && n.Type == "TenderEvaluation" && n.Link == link);
            if (!sent)
                await notifications.NotifyAsync(member.UserId, "TenderEvaluation", "Evaluation brief shared",
                    $"The tender and internal evaluation questions for {tender.TenderNumber}: {tender.Title} are available. Offer evaluation follows compliance review.",
                    link);
        }
    }

    private async Task LoadOptions(TenderSetup model)
    {
        model.Requests = await db.PurchaseRequisitions.AsNoTracking().Where(r => r.Status == "Approved")
            .OrderBy(r => r.RequestNumber)
            .Select(r => new SelectListItem(r.RequestNumber + " · " + r.Title, r.Id.ToString())).ToListAsync();
        model.FixedReviewers = await LoadFixedReviewers();

        var request = await db.PurchaseRequisitions.AsNoTracking()
            .Where(row => row.Id == model.RequestId && row.Status == Statuses.RequestStatus.Approved)
            .Select(row => new { row.DepartmentId, row.Department.Name, row.RequestNumber, row.Reason, row.TargetDate, row.SubmittedAt, row.CreatedAt,
                row.TechnicalEvaluationWeight, row.FinancialEvaluationWeight, row.EvaluationFocus }).SingleOrDefaultAsync();
        if (request is null) return;
        model.RequestTargetDate = request.TargetDate;
        model.RequestSubmittedAt = request.SubmittedAt ?? request.CreatedAt;
        model.RequestReason = request.Reason;
        model.RequestNumber = request.RequestNumber;
        model.DepartmentName = request.Name;
        model.TechnicalWeight = request.TechnicalEvaluationWeight ?? -1;
        model.FinancialWeight = request.FinancialEvaluationWeight ?? 0;
        model.EvaluationFocus = request.EvaluationFocus ?? "";
        model.SupplierCategories = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == Statuses.SupplierStatus.Approved && s.User.IsActive)
            .Select(s => s.Category).Distinct().OrderBy(c => c).ToListAsync();
        model.Suppliers = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == Statuses.SupplierStatus.Approved && s.User.IsActive)
            .OrderBy(s => s.CompanyName).ThenBy(s => s.Id)
            .Select(s => new SelectListItem(s.CompanyName, s.Id.ToString()) { Group = new SelectListGroup { Name = s.Category } })
            .ToListAsync();
        model.RequestItems = await db.PurchaseRequisitionItems.AsNoTracking()
            .Where(item => item.RequisitionId == model.RequestId).OrderBy(item => item.Id).ToListAsync();

        await LoadDepartmentReviewers(request.DepartmentId, model.FixedReviewers, model.DepartmentReviewers);
        // One Evaluation Committee account in the department = he is the Department Manager by default.
        if (model.DepartmentReviewers.Count == 1 && string.IsNullOrEmpty(model.DepartmentReviewerId))
            model.DepartmentReviewerId = model.DepartmentReviewers[0].Value;
    }

    private async Task LoadDepartmentReviewers(int departmentId, IEnumerable<ApplicationUser?> fixedReviewers,
        List<SelectListItem> options)
    {
        var fixedIds = fixedReviewers.OfType<ApplicationUser>().Select(user => user.Id).ToArray();
        var accounts = await CommitteeDirectory.EligibleEvaluators(db)
            .Where(user => user.DepartmentId == departmentId && !fixedIds.Contains(user.Id)).ToListAsync();
        foreach (var account in accounts
            .OrderByDescending(user => string.Equals(user.JobTitle, "Department Manager", StringComparison.OrdinalIgnoreCase))
            .ThenBy(user => user.FullName))
        {
            var position = string.IsNullOrWhiteSpace(account.JobTitle) ? "Evaluation Committee" : account.JobTitle;
            options.Add(new SelectListItem($"{account.FullName} · {position}", account.Id));
        }
    }

    // The three fixed positions are the people the Admin put in the boxes of Admin > Evaluation committee (in the order of TenderSetup.Seats[1..]).
    private Task<ApplicationUser?[]> LoadFixedReviewers() => CommitteeDirectory.FixedReviewersAsync(db);

    private static string CommitteeAssignmentMessage(ApplicationUser?[] reviewers)
    {
        var unresolved = TenderSetup.Seats.Skip(1)
            .Where((_, index) => reviewers[index] is null)
            .Select(seat => seat.Title).ToArray();
        return unresolved.Length == 0
            ? "Select an eligible department representative before publishing."
            : $"The Admin has not assigned an active Evaluation Committee member to: {string.Join(", ", unresolved)} (Admin > Evaluation committee). You can still save a draft.";
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
        await NotifyInvitedSuppliers(id, selected);
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = $"{selected.Length} invitation(s) sent.";
        return destination;
    }

    private async Task<bool> CanInviteSuppliers(Tender tender, string? category, int[] selected)
    {
        if (tender.Invitations.Any(invitation => selected.Contains(invitation.SupplierProfileId))) return false;
        var approved = await db.SupplierProfiles.CountAsync(supplier => selected.Contains(supplier.Id)
            && supplier.Status == "Approved" && supplier.User.IsActive && (string.IsNullOrEmpty(category) || supplier.Category.Trim() == category));
        return approved == selected.Length;
    }

    private async Task NotifyInvitedSuppliers(int tenderId, int[] supplierIds)
    {
        var tender = await db.Tenders.AsNoTracking().SingleAsync(t => t.Id == tenderId);
        var recipients = await db.SupplierProfiles.Where(s => supplierIds.Contains(s.Id)).Select(s => s.UserId).ToListAsync();
        foreach (var userId in recipients)
            await notifications.NotifyAsync(userId, "TenderInvitation", "Invitation to tender",
                $"You are invited to {tender.TenderNumber}: {tender.Title}. Review the requirements and submit your offer before the deadline.",
                $"/Sourcing/TenderNotice/Index/{tenderId}");
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
