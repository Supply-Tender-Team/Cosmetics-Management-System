using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Manager.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Manager.Controllers;

[Area("Manager")]
[Authorize(Roles = Roles.SourcingManager)]
public class DecisionsController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;

    public DecisionsController(ApplicationDbContext db, AuditService audit, NotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
    }

    public async Task<IActionResult> Index(int? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (id == null)
        {
            var queue = new ManagerReportsQueueViewModel
            {
                PendingReports = await _db.EvaluationReports
                    .AsNoTracking()
                    .Where(report => report.Status == Statuses.ReportStatus.RecommendationPending
                        && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                        && report.Tender.CommitteeMembers.Any(member => member.UserId == userId
                            && member.MemberRole == Statuses.CommitteeRole.SourcingManager))
                    .OrderByDescending(report => report.SubmittedAt ?? report.CreatedAt)
                    .Select(report => new ManagerPendingReportViewModel
                    {
                        Id = report.Id,
                        TenderId = report.TenderId,
                        TenderNumber = report.Tender.TenderNumber,
                        TenderTitle = report.Tender.Title,
                        RequestNumber = report.Tender.Requisition.RequestNumber,
                        RequestTitle = report.Tender.Requisition.Title,
                        DepartmentName = report.Tender.Requisition.Department.Name,
                        EstimatedValue = report.Tender.EstimatedValue,
                        RequiresTopCommittee = report.Tender.RequiresTopCommittee,
                        SubmittedAt = report.SubmittedAt ?? report.CreatedAt
                    })
                    .ToListAsync()
            };

            return View("Queue", queue);
        }

        var model = await BuildPageModelAsync(id.Value, userId);
        if (model == null)
            return NotFound();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, ManagerDecisionInputViewModel input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (input.Decision != Statuses.ApprovalDecision.Approved
            && input.Decision != Statuses.ApprovalDecision.Rejected
            && input.Decision != Statuses.ApprovalDecision.Returned)
        {
            ModelState.AddModelError(nameof(input.Decision), "Choose a valid decision.");
        }

        var report = await _db.EvaluationReports
            .Include(item => item.Tender)
            .FirstOrDefaultAsync(item => item.TenderId == id
                && item.Status == Statuses.ReportStatus.RecommendationPending
                && item.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                && item.Tender.CommitteeMembers.Any(member => member.UserId == userId
                    && member.MemberRole == Statuses.CommitteeRole.SourcingManager));

        if (report == null)
            return NotFound();

        var recommendedSupplierId = report.RecommendedBidId.HasValue
            ? await _db.Bids
                .Where(bid => bid.Id == report.RecommendedBidId.Value && bid.TenderId == id)
                .Select(bid => (int?)bid.SupplierProfileId)
                .FirstOrDefaultAsync()
            : null;

        if (input.Decision == Statuses.ApprovalDecision.Approved && !recommendedSupplierId.HasValue)
            ModelState.AddModelError(string.Empty, "The report must include a recommended supplier before it can be approved.");

        if (!ModelState.IsValid)
        {
            var page = await BuildPageModelAsync(id, userId, input);
            if (page == null)
                return NotFound();
            return View("Index", page);
        }

        var approval = new TenderApproval
        {
            TenderId = report.TenderId,
            ReportId = report.Id,
            DecidedByUserId = userId,
            Level = Statuses.ApprovalLevel.Manager,
            Decision = input.Decision,
            SelectedBidId = input.Decision == Statuses.ApprovalDecision.Approved ? report.RecommendedBidId : null,
            Comment = input.Comment.Trim(),
            DecidedAt = DateTime.UtcNow
        };

        if (input.Decision == Statuses.ApprovalDecision.Approved)
        {
            report.Status = Statuses.ReportStatus.ManagerApproved;
            if (report.Tender.RequiresTopCommittee)
            {
                report.Tender.Status = Statuses.TenderStatus.AwaitingCommitteeDecision;
            }
            else
            {
                report.Tender.Status = Statuses.TenderStatus.Awarded;
                report.Tender.AwardedSupplierId = recommendedSupplierId;
            }
        }
        else if (input.Decision == Statuses.ApprovalDecision.Rejected)
        {
            report.Status = Statuses.ReportStatus.ManagerRejected;
            report.Tender.Status = Statuses.TenderStatus.Cancelled;
        }
        else
        {
            report.Status = Statuses.ReportStatus.ReturnedForReview;
            report.Tender.Status = Statuses.TenderStatus.UnderEvaluation;
        }

        _db.TenderApprovals.Add(approval);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "ManagerDecision",
            "TenderApproval",
            approval.Id.ToString(),
            $"{approval.Decision} for {report.Tender.TenderNumber}. {approval.Comment}");

        if (input.Decision == Statuses.ApprovalDecision.Approved && report.Tender.RequiresTopCommittee)
        {
            await _notifications.NotifyRoleAsync(
                Roles.TopCommittee,
                "DecisionRequired",
                "Tender forwarded for review",
                $"{report.Tender.TenderNumber} is ready for Top Committee review.",
                "/Committee/Dashboard");
        }
        else if (input.Decision == Statuses.ApprovalDecision.Rejected
            || input.Decision == Statuses.ApprovalDecision.Returned)
        {
            await _notifications.NotifyAsync(
                report.PreparedByUserId,
                "ManagerDecisionRecorded",
                "Manager decision recorded",
                $"{report.Tender.TenderNumber}: {input.Decision}. Open the evaluation dashboard to review the manager comment.",
                "/Evaluation/Dashboard");
        }

        TempData["ManagerDecisionMessage"] = input.Decision switch
        {
            Statuses.ApprovalDecision.Approved when report.Tender.RequiresTopCommittee => "Report approved and forwarded to the Top Committee.",
            Statuses.ApprovalDecision.Approved => "Report approved and tender awarded to the recommended supplier.",
            Statuses.ApprovalDecision.Rejected => "Report rejected and tender closed.",
            _ => "Report returned to the evaluation team for review."
        };

        return RedirectToAction("Index", "Dashboard");
    }

    private async Task<ManagerDecisionPageViewModel?> BuildPageModelAsync(int tenderId, string userId, ManagerDecisionInputViewModel? input = null)
    {
        return await _db.EvaluationReports
            .AsNoTracking()
            .Where(report => report.TenderId == tenderId
                && report.Status == Statuses.ReportStatus.RecommendationPending
                && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                && report.Tender.CommitteeMembers.Any(member => member.UserId == userId
                    && member.MemberRole == Statuses.CommitteeRole.SourcingManager))
            .OrderByDescending(report => report.SubmittedAt ?? report.CreatedAt)
            .Select(report => new ManagerDecisionPageViewModel
            {
                TenderId = report.TenderId,
                ReportId = report.Id,
                TenderNumber = report.Tender.TenderNumber,
                TenderTitle = report.Tender.Title,
                RequestNumber = report.Tender.Requisition.RequestNumber,
                RequestTitle = report.Tender.Requisition.Title,
                DepartmentName = report.Tender.Requisition.Department.Name,
                ReportStatus = report.Status,
                EstimatedValue = report.Tender.EstimatedValue,
                RequiresTopCommittee = report.Tender.RequiresTopCommittee,
                RecommendedSupplier = report.RecommendedBid == null ? null : report.RecommendedBid.SupplierProfile.CompanyName,
                RecommendedBidPrice = report.RecommendedBid == null ? null : report.RecommendedBid.TotalPrice,
                RecommendedBidCurrency = report.RecommendedBid == null ? null : report.RecommendedBid.Currency,
                Input = input ?? new ManagerDecisionInputViewModel()
            })
            .FirstOrDefaultAsync();
    }
}
