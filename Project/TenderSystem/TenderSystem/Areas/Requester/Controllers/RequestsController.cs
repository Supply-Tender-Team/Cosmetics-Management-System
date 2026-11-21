using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Requester.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Requester.Controllers;

[Area("Requester")]
[Authorize(Roles = Roles.Requester)]
public class RequestsController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;

    private readonly AuditService _audit;
    private readonly NotificationService _notify;

    public RequestsController(ApplicationDbContext db, AuditService audit, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? searchTerm, string? status)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var availableStatuses = new[]
        {
            "Draft", "Submitted", "Under Review", "Approved", "Rejected", "Returned", "Converted to Tender"
        };

        status = availableStatuses.FirstOrDefault(availableStatus =>
            string.Equals(availableStatus, status, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        var requests = _db.PurchaseRequisitions
            .AsNoTracking()
            .Where(request => request.RequestedByUserId == userId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            searchTerm = searchTerm.Trim();
            requests = requests.Where(request => request.RequestNumber.Contains(searchTerm) || request.Title.Contains(searchTerm));
        }

        if (!string.IsNullOrWhiteSpace(status))
            requests = requests.Where(request => request.Status == status);

        var model = new MyRequestsViewModel
        {
            SearchTerm = searchTerm ?? string.Empty,
            Status = status,
            Requests = await requests
                .OrderByDescending(request => request.CreatedAt)
                .Select(request => new PurchaseRequestDetailsViewModel
                {
                    Id = request.Id,
                    RequestNumber = request.RequestNumber,
                    Title = request.Title,
                    Reason = request.Reason,
                    Priority = request.Priority,
                    Status = request.Status,
                    DepartmentName = request.Department.Name,
                    CreatedAt = request.CreatedAt,
                    TargetDate = request.TargetDate,
                    SubmittedAt = request.SubmittedAt,
                    EstimatedBudget = request.EstimatedBudget,
                    TechnicalEvaluationWeight = request.TechnicalEvaluationWeight,
                    FinancialEvaluationWeight = request.FinancialEvaluationWeight,
                    EvaluationFocus = request.EvaluationFocus,
                    ReviewNote = request.ReviewNote,
                    Items = request.PurchaseRequisitionItems
                        .OrderBy(item => item.Id)
                        .Select(item => new PurchaseRequestDetailsItemViewModel
                        {
                            ItemName = item.ItemName,
                            Quantity = item.Quantity,
                            Unit = item.Unit,
                            Specifications = item.Specifications
                        })
                        .ToList()
                })
                .ToListAsync()
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreatePurchaseRequestViewModel
        {
            TargetDate = DateTime.Today.AddDays(7)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var request = await _db.PurchaseRequisitions
            .AsNoTracking()
            .Include(item => item.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(item => item.Id == id && item.RequestedByUserId == userId);

        if (request is null)
            return NotFound();
        if (request.Status is not Statuses.RequestStatus.Draft and not Statuses.RequestStatus.Submitted and not Statuses.RequestStatus.Returned)
            return BadRequest("Only draft, submitted, or returned requests can be edited before review is completed.");

        return View("Create", new CreatePurchaseRequestViewModel
        {
            Id = request.Id,
            Title = request.Title,
            Reason = request.Reason,
            Priority = request.Priority,
            TargetDate = request.TargetDate,
            TechnicalEvaluationWeight = request.TechnicalEvaluationWeight,
            FinancialEvaluationWeight = request.FinancialEvaluationWeight,
            EvaluationFocus = request.EvaluationFocus ?? string.Empty,
            Items = request.PurchaseRequisitionItems
                .OrderBy(item => item.Id)
                .Select(item => new CreatePurchaseRequestItemViewModel
                {
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    Specifications = item.Specifications
                })
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePurchaseRequestViewModel model, string submitAction)
    {
        model.Items ??= [];

        var isSubmit = string.Equals(submitAction, "submit", StringComparison.OrdinalIgnoreCase);
        var isDraft = string.Equals(submitAction, "draft", StringComparison.OrdinalIgnoreCase);

        if (!isSubmit && !isDraft)
            ModelState.AddModelError(string.Empty, "Choose whether to save the request as a draft or submit it.");

        if (isDraft)
        {
            // A draft is intentionally allowed to be incomplete. Keep only the
            // lightweight checks for values the requester did provide.
            ModelState.Clear();
            ValidateDraftValues(model);
        }
        else if (model.TargetDate.HasValue && model.TargetDate.Value.Date < DateTime.Today)
        {
            ModelState.AddModelError(nameof(model.TargetDate), "The target date must be today or later.");
        }

        if (isSubmit)
            ValidateEvaluationPreferences(model);

        if (!ModelState.IsValid)
            return View(model);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var departmentId = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DepartmentId)
            .SingleOrDefaultAsync();

        if (departmentId is null && !model.Id.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Your account must be assigned to a department before creating a request.");
            return View(model);
        }

        PurchaseRequisition request;
        var isUpdatingDraft = model.Id.HasValue;

        if (isUpdatingDraft)
        {
            var draftRequest = await _db.PurchaseRequisitions
                .Include(item => item.PurchaseRequisitionItems)
                .SingleOrDefaultAsync(item => item.Id == model.Id!.Value && item.RequestedByUserId == userId);

            if (draftRequest is null)
                return NotFound();
            if (draftRequest.Status is not Statuses.RequestStatus.Draft and not Statuses.RequestStatus.Submitted and not Statuses.RequestStatus.Returned)
                return BadRequest("Only draft, submitted, or returned requests can be edited before review is completed.");

            request = draftRequest;
            _db.PurchaseRequisitionItems.RemoveRange(request.PurchaseRequisitionItems);
            request.Title = model.Title ?? string.Empty;
            request.Reason = model.Reason ?? string.Empty;
            request.Priority = model.Priority ?? string.Empty;
            request.TargetDate = model.TargetDate?.Date ?? DateTime.Today;
            request.TechnicalEvaluationWeight = model.TechnicalEvaluationWeight;
            request.FinancialEvaluationWeight = model.FinancialEvaluationWeight;
            request.EvaluationFocus = model.EvaluationFocus?.Trim();
            request.Status = isSubmit ? Statuses.RequestStatus.Submitted : Statuses.RequestStatus.Draft;
            request.SubmittedAt = isSubmit ? DateTime.UtcNow : null;
            request.PurchaseRequisitionItems = model.Items.Select(item => new PurchaseRequisitionItem
            {
                ItemName = item.ItemName ?? string.Empty,
                Quantity = item.Quantity.GetValueOrDefault(),
                Unit = item.Unit ?? string.Empty,
                Specifications = item.Specifications ?? string.Empty
            }).ToList();
        }
        else
        {
            request = new PurchaseRequisition
            {
                RequestNumber = $"PR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..26],
                Title = model.Title ?? string.Empty,
                Reason = model.Reason ?? string.Empty,
                Priority = model.Priority ?? string.Empty,
                TargetDate = model.TargetDate?.Date ?? DateTime.Today,
                TechnicalEvaluationWeight = model.TechnicalEvaluationWeight,
                FinancialEvaluationWeight = model.FinancialEvaluationWeight,
                EvaluationFocus = model.EvaluationFocus?.Trim(),
                Status = isSubmit ? Statuses.RequestStatus.Submitted : Statuses.RequestStatus.Draft,
                DepartmentId = departmentId!.Value,
                RequestedByUserId = userId,
                SubmittedAt = isSubmit ? DateTime.UtcNow : null,
                PurchaseRequisitionItems = model.Items.Select(item => new PurchaseRequisitionItem
                {
                    ItemName = item.ItemName ?? string.Empty,
                    Quantity = item.Quantity.GetValueOrDefault(),
                    Unit = item.Unit ?? string.Empty,
                    Specifications = item.Specifications ?? string.Empty
                }).ToList()
            };
            _db.PurchaseRequisitions.Add(request);
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync(isSubmit ? "Submitted" : isUpdatingDraft ? "Updated" : "Created", "PurchaseRequisition", request.Id.ToString(), request.RequestNumber);
        if (isSubmit)
            await _notify.NotifyRoleAsync(Roles.SourcingOfficer, "RequestSubmitted", "New purchase request",
                $"{request.Title} ({request.RequestNumber}) was submitted.", "/Sourcing/Requests");

        TempData["RequestSuccessMessage"] = isSubmit
            ? $"Request {request.RequestNumber} was submitted for review."
            : $"Request {request.RequestNumber} was saved as a draft.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraft(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var draft = await _db.PurchaseRequisitions
            .Include(request => request.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(request => request.Id == id
                && request.RequestedByUserId == userId);

        if (draft is null)
            return NotFound();
        if (draft.Status is not Statuses.RequestStatus.Draft and not Statuses.RequestStatus.Submitted and not Statuses.RequestStatus.Returned)
            return BadRequest("Only draft, submitted, or returned requests can be deleted before review is completed.");

        if (await _db.Tenders.AnyAsync(tender => tender.RequisitionId == id))
            return BadRequest("This request has already been converted to a tender and cannot be deleted.");

        var requestNumber = draft.RequestNumber;
        _db.PurchaseRequisitionItems.RemoveRange(draft.PurchaseRequisitionItems);
        _db.PurchaseRequisitions.Remove(draft);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Deleted", "PurchaseRequisition", id.ToString(), requestNumber);
        TempData["RequestSuccessMessage"] = $"Request {requestNumber} was deleted.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateDraftValues(CreatePurchaseRequestViewModel model)
    {
        if ((model.Title?.Length ?? 0) > 200)
            ModelState.AddModelError(nameof(model.Title), "The request title cannot exceed 200 characters.");

        if ((model.Reason?.Length ?? 0) > 1000)
            ModelState.AddModelError(nameof(model.Reason), "The reason cannot exceed 1000 characters.");

        if (model.TechnicalEvaluationWeight is decimal technical
            && (technical < 0 || technical > 100))
            ModelState.AddModelError(nameof(model.TechnicalEvaluationWeight), "Technical weight must be between 0 and 100.");

        if (model.FinancialEvaluationWeight is decimal financial
            && (financial < 0 || financial > 100))
            ModelState.AddModelError(nameof(model.FinancialEvaluationWeight), "Financial weight must be between 0 and 100.");

        if ((model.EvaluationFocus?.Length ?? 0) > 2000)
            ModelState.AddModelError(nameof(model.EvaluationFocus), "Evaluation focus cannot exceed 2000 characters.");

        if (model.TechnicalEvaluationWeight.HasValue && model.FinancialEvaluationWeight.HasValue
            && decimal.Round(model.TechnicalEvaluationWeight.Value + model.FinancialEvaluationWeight.Value, 2) != 100m)
            ModelState.AddModelError(string.Empty, "Technical and financial weights must total 100%.");

        if (!string.IsNullOrWhiteSpace(model.Priority)
            && !new[] { "Low", "Medium", "High", "Urgent" }.Contains(model.Priority, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Priority), "Choose a valid priority.");
        }

        if (model.TargetDate.HasValue && model.TargetDate.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.TargetDate), "The target date must be today or later.");

        if (model.Items is null)
            return;

        for (var index = 0; index < model.Items.Count; index++)
        {
            var item = model.Items[index];
            if ((item.ItemName?.Length ?? 0) > 200)
                ModelState.AddModelError($"Items[{index}].ItemName", "The item name cannot exceed 200 characters.");

            if (item.Quantity.HasValue && item.Quantity.Value <= 0)
                ModelState.AddModelError($"Items[{index}].Quantity", "Quantity must be greater than zero.");

            if ((item.Unit?.Length ?? 0) > 30)
                ModelState.AddModelError($"Items[{index}].Unit", "The unit cannot exceed 30 characters.");
            else if (!string.IsNullOrWhiteSpace(item.Unit)
                && !CreatePurchaseRequestItemViewModel.IsAllowedUnit(item.Unit))
                ModelState.AddModelError($"Items[{index}].Unit", "Choose a valid quantity unit.");
        }
    }

    private void ValidateEvaluationPreferences(CreatePurchaseRequestViewModel model)
    {
        if (!model.TechnicalEvaluationWeight.HasValue)
            ModelState.AddModelError(nameof(model.TechnicalEvaluationWeight), "Enter the technical evaluation weight.");

        if (!model.FinancialEvaluationWeight.HasValue)
            ModelState.AddModelError(nameof(model.FinancialEvaluationWeight), "Enter the financial evaluation weight.");

        if (model.TechnicalEvaluationWeight.HasValue && model.FinancialEvaluationWeight.HasValue
            && decimal.Round(model.TechnicalEvaluationWeight.Value + model.FinancialEvaluationWeight.Value, 2) != 100m)
            ModelState.AddModelError(string.Empty, "Technical and financial weights must total 100%.");

        if (string.IsNullOrWhiteSpace(model.EvaluationFocus))
            ModelState.AddModelError(nameof(model.EvaluationFocus), "Describe what matters most in the evaluation.");
    }

    [HttpGet]
    public async Task<IActionResult> Details(int? id)
    {
        if (id is null || id <= 0)
            return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var model = await _db.PurchaseRequisitions
            .AsNoTracking()
            .Where(request => request.Id == id.Value && request.RequestedByUserId == userId)
            .Select(request => new PurchaseRequestDetailsViewModel
            {
                Id = request.Id,
                RequestNumber = request.RequestNumber,
                Title = request.Title,
                Reason = request.Reason,
                Priority = request.Priority,
                Status = request.Status,
                DepartmentName = request.Department.Name,
                TargetDate = request.TargetDate,
                CreatedAt = request.CreatedAt,
                SubmittedAt = request.SubmittedAt,
                EstimatedBudget = request.EstimatedBudget,
                TechnicalEvaluationWeight = request.TechnicalEvaluationWeight,
                FinancialEvaluationWeight = request.FinancialEvaluationWeight,
                EvaluationFocus = request.EvaluationFocus,
                ReviewNote = request.ReviewNote,
                Items = request.PurchaseRequisitionItems
                    .OrderBy(item => item.Id)
                    .Select(item => new PurchaseRequestDetailsItemViewModel
                    {
                        ItemName = item.ItemName,
                        Quantity = item.Quantity,
                        Unit = item.Unit,
                        Specifications = item.Specifications
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync();

        if (model is null)
            return NotFound();

        return View(model);
    }
}
