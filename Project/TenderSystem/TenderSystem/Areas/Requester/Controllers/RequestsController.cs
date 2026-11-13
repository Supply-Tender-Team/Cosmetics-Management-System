using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Requester.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Requester.Controllers;

[Area("Requester")]
[Authorize(Roles = Roles.Requester)]
public class RequestsController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;

    public RequestsController(ApplicationDbContext db)
    {
        _db = db;
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePurchaseRequestViewModel model, string submitAction)
    {
        var isSubmit = string.Equals(submitAction, "submit", StringComparison.OrdinalIgnoreCase);
        var isDraft = string.Equals(submitAction, "draft", StringComparison.OrdinalIgnoreCase);

        if (!isSubmit && !isDraft)
            ModelState.AddModelError(string.Empty, "Choose whether to save the request as a draft or submit it.");

        if (model.TargetDate.HasValue && model.TargetDate.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.TargetDate), "The target date must be today or later.");

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

        if (departmentId is null)
        {
            ModelState.AddModelError(string.Empty, "Your account must be assigned to a department before creating a request.");
            return View(model);
        }

        var request = new PurchaseRequisition
        {
            RequestNumber = $"PR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..26],
            Title = model.Title,
            Reason = model.Reason,
            Priority = model.Priority,
            TargetDate = model.TargetDate.GetValueOrDefault().Date,
            Status = isSubmit ? "Submitted" : "Draft",
            DepartmentId = departmentId.Value,
            RequestedByUserId = userId,
            SubmittedAt = isSubmit ? DateTime.UtcNow : null,
            PurchaseRequisitionItems = model.Items.Select(item => new PurchaseRequisitionItem
            {
                ItemName = item.ItemName,
                Quantity = item.Quantity.GetValueOrDefault(),
                Unit = item.Unit,
                Specifications = item.Specifications
            }).ToList()
        };

        // TODO: Record this action in AuditService and notify Sourcing when those services are available.
        _db.PurchaseRequisitions.Add(request);
        await _db.SaveChangesAsync();

        TempData["RequestSuccessMessage"] = isSubmit
            ? $"Request {request.RequestNumber} was submitted for review."
            : $"Request {request.RequestNumber} was saved as a draft.";

        return RedirectToAction("Index", "Dashboard", new { area = "Requester" });
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
