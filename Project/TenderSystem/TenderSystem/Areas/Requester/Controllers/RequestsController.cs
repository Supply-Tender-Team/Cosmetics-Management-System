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

    private readonly ApplicationDbContext _context;

    private readonly AuditService _auditService;
    private readonly NotificationService _notificationService;

    public RequestsController(ApplicationDbContext context, AuditService auditService, NotificationService notificationService)
    {
        _context = context;
        _auditService = auditService;
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? searchTerm, string? status)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var supportedStatuses = new[]
        {
            "Draft", "Submitted", "Approved", "Rejected", "Returned", "Converted to Tender"
        };

        status = supportedStatuses.FirstOrDefault(supportedStatus =>
            string.Equals(supportedStatus, status, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        var requestQuery = _context.PurchaseRequisitions
            .AsNoTracking()
            .Where(request => request.RequestedByUserId == userId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            searchTerm = searchTerm.Trim();
            requestQuery = requestQuery.Where(request => request.RequestNumber.Contains(searchTerm) || request.Title.Contains(searchTerm));
        }

        if (!string.IsNullOrWhiteSpace(status))
            requestQuery = requestQuery.Where(request => request.Status == status);

        var model = new MyRequestsViewModel
        {
            SearchTerm = searchTerm ?? string.Empty,
            Status = status,
            Requests = await ProjectRequestDetails(requestQuery)
                .OrderByDescending(request => request.CreatedAt)
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

        var purchaseRequest = await _context.PurchaseRequisitions
            .AsNoTracking()
            .Include(request => request.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(request => request.Id == id && request.RequestedByUserId == userId);

        if (purchaseRequest is null)
            return NotFound();
        if (purchaseRequest.Status is not Statuses.RequestStatus.Draft and not Statuses.RequestStatus.Submitted and not Statuses.RequestStatus.Returned)
            return BadRequest("Only draft, submitted, or returned requests can be edited before review is completed.");

        return View("Create", new CreatePurchaseRequestViewModel
        {
            Id = purchaseRequest.Id,
            Title = purchaseRequest.Title,
            Reason = purchaseRequest.Reason,
            Priority = purchaseRequest.Priority,
            TargetDate = purchaseRequest.TargetDate,
            TechnicalEvaluationWeight = purchaseRequest.TechnicalEvaluationWeight,
            FinancialEvaluationWeight = purchaseRequest.FinancialEvaluationWeight,
            EvaluationFocus = purchaseRequest.EvaluationFocus ?? string.Empty,
            Items = purchaseRequest.PurchaseRequisitionItems
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

        var isSubmitting = string.Equals(submitAction, "submit", StringComparison.OrdinalIgnoreCase);
        var isSavingDraft = string.Equals(submitAction, "draft", StringComparison.OrdinalIgnoreCase);

        if (!isSubmitting && !isSavingDraft)
            ModelState.AddModelError(string.Empty, "Choose whether to save the request as a draft or submit it.");

        if (isSavingDraft)
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

        if (!isSavingDraft)
            ValidateItemQuantities(model);

        if (isSubmitting)
            ValidateEvaluationPreferences(model);

        if (!ModelState.IsValid)
            return View(model);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var departmentId = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DepartmentId)
            .SingleOrDefaultAsync();

        if (departmentId is null && !model.Id.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Your account must be assigned to a department before creating a request.");
            return View(model);
        }

        PurchaseRequisition purchaseRequest;
        var isExistingRequest = model.Id.HasValue;

        if (isExistingRequest)
        {
            var existingRequest = await _context.PurchaseRequisitions
                .Include(item => item.PurchaseRequisitionItems)
                .SingleOrDefaultAsync(item => item.Id == model.Id!.Value && item.RequestedByUserId == userId);

            if (existingRequest is null)
                return NotFound();
            if (existingRequest.Status is not Statuses.RequestStatus.Draft and not Statuses.RequestStatus.Submitted and not Statuses.RequestStatus.Returned)
                return BadRequest("Only draft, submitted, or returned requests can be edited before review is completed.");

            purchaseRequest = existingRequest;
            _context.PurchaseRequisitionItems.RemoveRange(purchaseRequest.PurchaseRequisitionItems);
            purchaseRequest.Title = model.Title ?? string.Empty;
            purchaseRequest.Reason = model.Reason ?? string.Empty;
            purchaseRequest.Priority = model.Priority ?? string.Empty;
            purchaseRequest.TargetDate = model.TargetDate?.Date ?? DateTime.Today;
            purchaseRequest.TechnicalEvaluationWeight = model.TechnicalEvaluationWeight;
            purchaseRequest.FinancialEvaluationWeight = model.FinancialEvaluationWeight;
            purchaseRequest.EvaluationFocus = model.EvaluationFocus?.Trim();
            purchaseRequest.Status = isSubmitting ? Statuses.RequestStatus.Submitted : Statuses.RequestStatus.Draft;
            purchaseRequest.SubmittedAt = isSubmitting ? DateTime.UtcNow : null;
            purchaseRequest.PurchaseRequisitionItems = model.Items.Select(item => new PurchaseRequisitionItem
            {
                ItemName = item.ItemName ?? string.Empty,
                Quantity = item.Quantity.GetValueOrDefault(),
                Unit = item.Unit ?? string.Empty,
                Specifications = item.Specifications ?? string.Empty
            }).ToList();
        }
        else
        {
            purchaseRequest = new PurchaseRequisition
            {
                RequestNumber = $"PR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..26],
                Title = model.Title ?? string.Empty,
                Reason = model.Reason ?? string.Empty,
                Priority = model.Priority ?? string.Empty,
                TargetDate = model.TargetDate?.Date ?? DateTime.Today,
                TechnicalEvaluationWeight = model.TechnicalEvaluationWeight,
                FinancialEvaluationWeight = model.FinancialEvaluationWeight,
                EvaluationFocus = model.EvaluationFocus?.Trim(),
                Status = isSubmitting ? Statuses.RequestStatus.Submitted : Statuses.RequestStatus.Draft,
                DepartmentId = departmentId!.Value,
                RequestedByUserId = userId,
                SubmittedAt = isSubmitting ? DateTime.UtcNow : null,
                PurchaseRequisitionItems = model.Items.Select(item => new PurchaseRequisitionItem
                {
                    ItemName = item.ItemName ?? string.Empty,
                    Quantity = item.Quantity.GetValueOrDefault(),
                    Unit = item.Unit ?? string.Empty,
                    Specifications = item.Specifications ?? string.Empty
                }).ToList()
            };
            _context.PurchaseRequisitions.Add(purchaseRequest);
        }

        var isNewRequest = purchaseRequest.Id == 0;
        await _context.SaveChangesAsync();
        if (isNewRequest)
        {
            // the random number above only had to be unique for the first save: now the request has its id, so it gets a short readable number
            purchaseRequest.RequestNumber = $"PR-{purchaseRequest.CreatedAt:yyyy}-{purchaseRequest.Id:0000}";
            await _context.SaveChangesAsync();
        }

        await _auditService.LogAsync(isSubmitting ? "Submitted" : isExistingRequest ? "Updated" : "Created", "PurchaseRequisition", purchaseRequest.Id.ToString(), purchaseRequest.RequestNumber);
        if (isSubmitting)
            await _notificationService.NotifyRoleAsync(Roles.SourcingOfficer, "RequestSubmitted", "New purchase request",
                $"{purchaseRequest.Title} ({purchaseRequest.RequestNumber}) was submitted.", "/Sourcing/Requests");

        TempData["RequestSuccessMessage"] = isSubmitting
            ? $"Request {purchaseRequest.RequestNumber} was submitted for review."
            : $"Request {purchaseRequest.RequestNumber} was saved as a draft.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraft(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var draft = await _context.PurchaseRequisitions
            .Include(request => request.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(request => request.Id == id
                && request.RequestedByUserId == userId);

        if (draft is null)
            return NotFound();
        if (draft.Status is not Statuses.RequestStatus.Draft and not Statuses.RequestStatus.Submitted and not Statuses.RequestStatus.Returned)
            return BadRequest("Only draft, submitted, or returned requests can be deleted before review is completed.");

        if (await _context.Tenders.AnyAsync(tender => tender.RequisitionId == id))
            return BadRequest("This request has already been converted to a tender and cannot be deleted.");

        var requestNumber = draft.RequestNumber;
        _context.PurchaseRequisitionItems.RemoveRange(draft.PurchaseRequisitionItems);
        _context.PurchaseRequisitions.Remove(draft);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Deleted", "PurchaseRequisition", id.ToString(), requestNumber);
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

        ValidateItemQuantities(model);

        if (model.Items is null)
            return;

        for (var index = 0; index < model.Items.Count; index++)
        {
            var requestItem = model.Items[index];
            if ((requestItem.ItemName?.Length ?? 0) > 200)
                ModelState.AddModelError($"Items[{index}].ItemName", "The item name cannot exceed 200 characters.");

            if ((requestItem.Unit?.Length ?? 0) > 30)
                ModelState.AddModelError($"Items[{index}].Unit", "The unit cannot exceed 30 characters.");
            else if (!string.IsNullOrWhiteSpace(requestItem.Unit)
                && !CreatePurchaseRequestItemViewModel.IsAllowedUnit(requestItem.Unit))
                ModelState.AddModelError($"Items[{index}].Unit", "Choose a valid quantity unit.");
        }
    }

    private void ValidateItemQuantities(CreatePurchaseRequestViewModel model)
    {
        if (model.Items is null)
            return;

        for (var index = 0; index < model.Items.Count; index++)
        {
            var quantity = model.Items[index].Quantity;
            if (quantity.HasValue
                && (quantity.Value < 1m || quantity.Value != decimal.Truncate(quantity.Value)))
            {
                ModelState.AddModelError($"Items[{index}].Quantity", "Quantity must be a whole number starting from 1.");
            }
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

        var model = await ProjectRequestDetails(_context.PurchaseRequisitions
            .AsNoTracking()
            .Where(request => request.Id == id.Value && request.RequestedByUserId == userId))
            .SingleOrDefaultAsync();

        if (model is null)
            return NotFound();

        return View(model);
    }

    private static IQueryable<PurchaseRequestDetailsViewModel> ProjectRequestDetails(
        IQueryable<PurchaseRequisition> requests)
    {
        return requests.Select(request => new PurchaseRequestDetailsViewModel
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
        });
    }
}
