using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Controllers;

[Area("Committee")]
[Authorize(Roles = Roles.TopCommittee)]
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
        var pending = PendingPackages();
        var model = new TopCommitteeDashboardViewModel
        {
            PendingPackageCount = await pending.CountAsync(),
            SupplierOfferCount = await pending.Select(tender => (int?)tender.Bids.Count).SumAsync() ?? 0,
            EstimatedValueTotal = await pending.Select(tender => (decimal?)tender.EstimatedValue).SumAsync() ?? 0,
            Packages = await PendingPackages()
                .OrderByDescending(tender => tender.TenderApprovals
                    .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                        && approval.Decision == Statuses.ApprovalDecision.Approved)
                    .Select(approval => (DateTime?)approval.DecidedAt)
                    .FirstOrDefault())
                .Take(8)
                .Select(tender => new TopCommitteePackageListItemViewModel
                {
                    TenderId = tender.Id,
                    TenderNumber = tender.TenderNumber,
                    TenderTitle = tender.Title,
                    RequestNumber = tender.Requisition.RequestNumber,
                    RequestTitle = tender.Requisition.Title,
                    DepartmentName = tender.Requisition.Department.Name,
                    EstimatedValue = tender.EstimatedValue,
                    BidCount = tender.Bids.Count,
                    RecommendedSupplier = tender.TenderApprovals
                        .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                            && approval.Decision == Statuses.ApprovalDecision.Approved)
                        .OrderByDescending(approval => approval.DecidedAt)
                        .Select(approval => approval.SelectedBid == null
                            ? string.Empty
                            : approval.SelectedBid.SupplierProfile.CompanyName)
                        .FirstOrDefault() ?? string.Empty,
                    RecommendedPrice = tender.TenderApprovals
                        .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                            && approval.Decision == Statuses.ApprovalDecision.Approved)
                        .OrderByDescending(approval => approval.DecidedAt)
                        .Select(approval => approval.SelectedBid == null
                            ? (decimal?)null
                            : approval.SelectedBid.TotalPrice)
                        .FirstOrDefault(),
                    RecommendedCurrency = tender.TenderApprovals
                        .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                            && approval.Decision == Statuses.ApprovalDecision.Approved)
                        .OrderByDescending(approval => approval.DecidedAt)
                        .Select(approval => approval.SelectedBid == null
                            ? null
                            : approval.SelectedBid.Currency)
                        .FirstOrDefault(),
                    ManagerName = tender.TenderApprovals
                        .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                            && approval.Decision == Statuses.ApprovalDecision.Approved)
                        .OrderByDescending(approval => approval.DecidedAt)
                        .Select(approval => approval.DecidedByUser.FullName)
                        .FirstOrDefault() ?? string.Empty,
                    ManagerComment = tender.TenderApprovals
                        .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                            && approval.Decision == Statuses.ApprovalDecision.Approved)
                        .OrderByDescending(approval => approval.DecidedAt)
                        .Select(approval => approval.Comment)
                        .FirstOrDefault() ?? string.Empty,
                    ManagerApprovedAt = tender.TenderApprovals
                        .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                            && approval.Decision == Statuses.ApprovalDecision.Approved)
                        .OrderByDescending(approval => approval.DecidedAt)
                        .Select(approval => (DateTime?)approval.DecidedAt)
                        .FirstOrDefault() ?? tender.CreatedAt
                })
                .ToListAsync()
        };

        return View(model);
    }

    private IQueryable<Tender> PendingPackages() => _db.Tenders
        .AsNoTracking()
        .Where(tender => tender.RequiresTopCommittee
            && tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
            && tender.EvaluationReports.Any(report => report.Status == Statuses.ReportStatus.ManagerApproved)
            && tender.TenderApprovals.Any(approval => approval.Level == Statuses.ApprovalLevel.Manager
                && approval.Decision == Statuses.ApprovalDecision.Approved));
}
