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
        if (model.Questions.Count == 0 || model.Questions.Sum(q => q.Weight) != 80 ||
            model.Questions.Any(q => decimal.Round(q.Weight, 2) != q.Weight))
            ModelState.AddModelError(nameof(model.Questions), "Technical weights must total exactly 80%, with up to two decimal places.");
        if (model.Reviewers.Count != 4 || model.Reviewers.Distinct().Count() != 4 ||
            model.Reviewers.Any(id => !model.Accounts.Any(account => account.Value == id)))
            ModelState.AddModelError(nameof(model.Reviewers), "Select four different Evaluation Committee accounts.");
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
            CommitteeMembers = TenderSetup.Seats.Select((seat, index) => new CommitteeMember
            {
                MemberRole = seat.Code, UserId = model.Reviewers[index]
            }).ToList()
        };
        tender.EvaluationCriteria.Add(new EvaluationCriterion
        {
            Name = "Lowest eligible quoted total", Type = "Financial", WeightPercent = 20, MaxScore = 1
        });
        if (publishNow)
        {
            tender.Status = "Published";
            tender.PublishDate = DateTime.UtcNow;
            foreach (var criterion in tender.EvaluationCriteria) criterion.IsLocked = true;
        }
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        await audit.LogAsync("Created", "Tender", tender.Id.ToString());
        if (publishNow) await ShareWithCommittee(tender);
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id = tender.Id });
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
        var lowest = comparable ? bids.Min(b => b.TotalPrice) : 0;
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
                    if (score is null || score.Score < 0 || score.Score > criterion.MaxScore)
                    {
                        complete = false;
                        break;
                    }
                    points += score.Score / criterion.MaxScore * criterion.WeightPercent;
                }
                if (complete) technical[i] = points;
            }
            decimal? average = technical.All(s => s.HasValue) ? technical.Average() : null;
            int? financial = comparable ? (bid.TotalPrice == lowest ? 20 : 0) : null;
            return new TenderOfferScore(bid.SupplierProfile.CompanyName, bid.TotalPrice, bid.Currency,
                technical, average, financial, average + financial);
        }).ToList();

        var categories = await db.SupplierProfiles.AsNoTracking()
            .Where(s => s.Status == "Approved" && s.Category.Trim() != "")
            .Select(s => s.Category.Trim()).Distinct().OrderBy(c => c).ToListAsync();
        ViewData["InvitationCategories"] = categories;
        category ??= categories.FirstOrDefault(c => string.Equals(c, tender.Requisition.Department.Name, StringComparison.OrdinalIgnoreCase));
        if (category is not null && !categories.Contains(category)) category = null;
        ViewData["InvitationCategory"] = category;
        ViewData["InvitableSuppliers"] = await db.SupplierProfiles.AsNoTracking()
            .Where(s => category != null && s.Status == "Approved" && s.Category.Trim() == category && !s.Invitations.Any(i => i.TenderId == id))
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
        if (tender.Status != "Draft" || tender.SubmissionDeadline <= DateTime.Now ||
            tender.EvaluationCriteria.Where(c => c.Type == "Technical").Sum(c => c.WeightPercent) != 80 ||
            tender.EvaluationCriteria.Where(c => c.Type == "Financial").Sum(c => c.WeightPercent) != 20 ||
            tender.EvaluationCriteria.Sum(c => c.WeightPercent) != 100 ||
            tender.CommitteeMembers.Count != 4 || tender.CommitteeMembers.Select(m => m.UserId).Distinct().Count() != 4)
        {
            TempData["TenderError"] = "Publish requires a draft with a future deadline, four reviewers and an 80/20 rubric.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var eligibleReviewers = (await users.GetUsersInRoleAsync(Roles.EvaluationCommittee)).Select(user => user.Id).ToHashSet();
        if (tender.CommitteeMembers.Any(member => !eligibleReviewers.Contains(member.UserId)))
        {
            TempData["TenderError"] = "All four assigned reviewers must still have the Evaluation Committee role.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var changed = await db.Tenders.Where(t => t.Id == id && t.Status == "Draft")
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, "Published")
                .SetProperty(t => t.PublishDate, DateTime.UtcNow));
        if (changed == 0) return RedirectToAction(nameof(Details), new { id });
        foreach (var criterion in tender.EvaluationCriteria) criterion.IsLocked = true;
        await db.SaveChangesAsync();
        await ShareWithCommittee(tender);
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ShareWithCommittee(Tender tender)
    {
        var link = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id = tender.Id })
            ?? throw new InvalidOperationException("Could not generate the evaluation brief link.");
        foreach (var member in tender.CommitteeMembers)
        {
            await notifications.NotifyAsync(member.UserId, "TenderEvaluation", "Evaluation brief shared",
                $"The evaluation brief for {tender.TenderNumber} — {tender.Title} is ready to review.", link);
        }
        await audit.LogAsync("Published", "Tender", tender.Id.ToString());
    }

    private async Task LoadOptions(TenderSetup model)
    {
        model.Requests = await db.PurchaseRequisitions.AsNoTracking().Where(r => r.Status == "Approved")
            .OrderBy(r => r.RequestNumber)
            .Select(r => new SelectListItem(r.RequestNumber + " · " + r.Title, r.Id.ToString())).ToListAsync();
        model.Accounts = (await users.GetUsersInRoleAsync(Roles.EvaluationCommittee)).OrderBy(u => u.FullName)
            .Select(u => new SelectListItem(u.FullName + " · " + u.Email, u.Id)).ToList();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(int id, string? category, int[] supplierIds)
    {
        var destination = RedirectToAction(nameof(Details), new { id, tab = "invitations", category });
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(category) || supplierIds.Length == 0)
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
        var approved = await db.SupplierProfiles.CountAsync(s => selected.Contains(s.Id) && s.Status == "Approved" && s.Category.Trim() == category);
        if (approved != selected.Length || tender.Invitations.Any(i => selected.Contains(i.SupplierProfileId)))
        {
            TempData["InvitationError"] = "The selection changed. Choose approved suppliers who have not already been invited.";
            return destination;
        }

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

        await db.SaveChangesAsync();
        await audit.LogAsync("Invitations sent", "Tender", id.ToString(), $"Suppliers invited: {selected.Length}");
        await transaction.CommitAsync();
        TempData["InvitationSuccess"] = $"{selected.Length} invitation(s) sent.";
        return destination;
    }

    public IActionResult Suppliers() => RedirectToAction(nameof(Index));
}
