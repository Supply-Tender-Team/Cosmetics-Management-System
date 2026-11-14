using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Manager.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Manager.Controllers;

[Area("Manager")]
[Authorize(Roles = Roles.SourcingManager)]
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

        var managerApprovals = _db.TenderApprovals
            .AsNoTracking()
            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager && approval.DecidedByUserId == userId);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var model = new ManagerDashboardViewModel
        {
            PendingReportCount = await _db.EvaluationReports
                .AsNoTracking()
                .CountAsync(report => report.Status == Statuses.ReportStatus.RecommendationPending
                    && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                    && report.Tender.CommitteeMembers.Any(member => member.UserId == userId
                        && member.MemberRole == Statuses.CommitteeRole.SourcingManager)),
            DecisionsThisMonth = await managerApprovals
                .CountAsync(approval => approval.DecidedAt >= monthStart),
            AwaitingTopCommitteeCount = await _db.Tenders
                .AsNoTracking()
                .CountAsync(tender => tender.RequiresTopCommittee
                    && tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                    && tender.TenderApprovals.Any(approval => approval.Level == Statuses.ApprovalLevel.Manager
                        && approval.DecidedByUserId == userId
                        && approval.Decision == Statuses.ApprovalDecision.Approved)),
            PendingReports = await _db.EvaluationReports
                .AsNoTracking()
                .Where(report => report.Status == Statuses.ReportStatus.RecommendationPending
                    && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                    && report.Tender.CommitteeMembers.Any(member => member.UserId == userId
                        && member.MemberRole == Statuses.CommitteeRole.SourcingManager))
                .OrderByDescending(report => report.SubmittedAt ?? report.CreatedAt)
                .Take(8)
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
                    SubmittedAt = report.SubmittedAt ?? report.CreatedAt,
                    TechnicalSummary = report.TechnicalSummary,
                    FinancialSummary = report.FinancialSummary,
                    Justification = report.Justification
                })
                .ToListAsync(),
            RecentDecisions = await managerApprovals
                .OrderByDescending(approval => approval.DecidedAt)
                .Take(5)
                .Select(approval => new ManagerDecisionHistoryViewModel
                {
                    TenderNumber = approval.Tender.TenderNumber,
                    TenderTitle = approval.Tender.Title,
                    Decision = approval.Decision,
                    Comment = approval.Comment,
                    DecidedAt = approval.DecidedAt
                })
                .ToListAsync(),
            TopCommitteeRejections = await _db.TenderApprovals
                .AsNoTracking()
                .Where(approval => approval.Level == Statuses.ApprovalLevel.TopCommittee
                    && approval.Decision == Statuses.ApprovalDecision.Rejected
                    && approval.Tender.TenderApprovals.Any(managerApproval =>
                        managerApproval.Level == Statuses.ApprovalLevel.Manager
                        && managerApproval.DecidedByUserId == userId))
                .OrderByDescending(approval => approval.DecidedAt)
                .Take(5)
                .Select(approval => new ManagerDecisionHistoryViewModel
                {
                    TenderNumber = approval.Tender.TenderNumber,
                    TenderTitle = approval.Tender.Title,
                    Decision = approval.Decision,
                    Comment = approval.Comment,
                    DecidedAt = approval.DecidedAt
                })
                .ToListAsync()
        };

        return View(model);
    }
}
