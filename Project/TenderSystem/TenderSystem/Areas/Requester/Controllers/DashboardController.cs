using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Requester.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Requester.Controllers;

[Area("Requester")]
[Authorize(Roles = Roles.Requester)]
public class DashboardController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var requester = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                DepartmentName = user.Department != null ? user.Department.Name : "Department"
            })
            .SingleOrDefaultAsync();

        var requests = _db.PurchaseRequisitions
            .AsNoTracking()
            .Where(request => request.RequestedByUserId == userId);

        var statusCounts = await requests
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count);

        var model = new RequesterDashboardViewModel
        {
            DepartmentName = requester?.DepartmentName ?? "Department",
            DraftRequests = CountFor("Draft"),
            SubmittedRequests = CountFor("Submitted"),
            UnderReviewRequests = CountFor("Under Review"),
            ApprovedRequests = CountFor("Approved"),
            ReturnedRequests = CountFor("Returned"),
            RejectedRequests = CountFor("Rejected"),
            ConvertedToTenderRequests = CountFor("Converted to Tender"),
            RecentRequests = await requests
                .OrderByDescending(request => request.CreatedAt)
                .Take(5)
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
                .ToListAsync(),
            RecentReviewNotes = await requests
                .Where(request => request.ReviewNote != null && request.ReviewNote != string.Empty)
                .OrderByDescending(request => request.Id)
                .Take(3)
                .Select(request => new RequesterDashboardReviewNoteViewModel
                {
                    Id = request.Id,
                    RequestNumber = request.RequestNumber,
                    Title = request.Title,
                    Note = request.ReviewNote ?? string.Empty
                })
                .ToListAsync(),
            ReviewNoteRequests = await requests
                .Where(request => request.ReviewNote != null && request.ReviewNote != string.Empty)
                .OrderByDescending(request => request.Id)
                .Take(3)
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
                .ToListAsync()
        };

        model.TotalRequests = statusCounts.Values.Sum();
        return View(model);

        int CountFor(string status) => statusCounts.GetValueOrDefault(status);
    }
}
