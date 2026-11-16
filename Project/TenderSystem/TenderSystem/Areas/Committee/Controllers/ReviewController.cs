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
public class ReviewController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;

    public ReviewController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int? id)
    {
        if (!id.HasValue)
        {
            var queue = new TopCommitteeReviewQueueViewModel
            {
                Packages = await PendingPackages()
                    .OrderByDescending(tender => tender.CreatedAt)
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

            return View("Queue", queue);
        }

        var tender = await PendingPackages()
            .Where(item => item.Id == id.Value)
            .Include(item => item.Requisition).ThenInclude(request => request.Department)
            .Include(item => item.Requisition).ThenInclude(request => request.RequestedByUser)
            .Include(item => item.Requisition).ThenInclude(request => request.PurchaseRequisitionItems)
            .Include(item => item.EvaluationReports.Where(report => report.Status == Statuses.ReportStatus.ManagerApproved))
                .ThenInclude(report => report.RecommendedBid).ThenInclude(bid => bid!.SupplierProfile)
            .Include(item => item.TenderApprovals.Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                && approval.Decision == Statuses.ApprovalDecision.Approved))
                .ThenInclude(approval => approval.DecidedByUser)
            .Include(item => item.TenderApprovals.Where(approval => approval.Level == Statuses.ApprovalLevel.Manager
                && approval.Decision == Statuses.ApprovalDecision.Approved))
                .ThenInclude(approval => approval.SelectedBid).ThenInclude(bid => bid!.SupplierProfile)
            .Include(item => item.Bids).ThenInclude(bid => bid.SupplierProfile)
            .Include(item => item.Bids).ThenInclude(bid => bid.BidItems).ThenInclude(bidItem => bidItem.RequisitionItem)
            .Include(item => item.Bids).ThenInclude(bid => bid.TechnicalEvaluations).ThenInclude(score => score.Criterion)
            .Include(item => item.Bids).ThenInclude(bid => bid.FinancialEvaluations)
            .Include(item => item.Bids).ThenInclude(bid => bid.ComplianceChecks)
            .Include(item => item.Bids).ThenInclude(bid => bid.BidDocuments)
            .Include(item => item.AiBidAnalyses).ThenInclude(analysis => analysis.Bid).ThenInclude(bid => bid.SupplierProfile)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        if (tender == null)
            return NotFound();

        var report = tender.EvaluationReports
            .OrderByDescending(item => item.SubmittedAt ?? item.CreatedAt)
            .FirstOrDefault();
        var managerApproval = tender.TenderApprovals
            .OrderByDescending(approval => approval.DecidedAt)
            .FirstOrDefault();

        if (report == null || managerApproval == null)
            return NotFound();

        var model = new TopCommitteeReviewPackageViewModel
        {
            TenderId = tender.Id,
            TenderNumber = tender.TenderNumber,
            TenderTitle = tender.Title,
            TenderStatus = tender.Status,
            EstimatedValue = tender.EstimatedValue,
            SubmissionDeadline = tender.SubmissionDeadline,
            RequestNumber = tender.Requisition.RequestNumber,
            RequestTitle = tender.Requisition.Title,
            DepartmentName = tender.Requisition.Department.Name,
            RequesterName = tender.Requisition.RequestedByUser.FullName,
            Priority = tender.Requisition.Priority,
            TargetDate = tender.Requisition.TargetDate,
            RequestReason = tender.Requisition.Reason,
            EstimatedBudget = tender.Requisition.EstimatedBudget,
            ReportStatus = report.Status,
            ReportSubmittedAt = report.SubmittedAt ?? report.CreatedAt,
            TechnicalSummary = report.TechnicalSummary,
            FinancialSummary = report.FinancialSummary,
            RecommendationJustification = report.Justification,
            RecommendedSupplier = managerApproval.SelectedBid?.SupplierProfile.CompanyName
                ?? report.RecommendedBid?.SupplierProfile.CompanyName
                ?? string.Empty,
            ManagerSelectedBidId = managerApproval.SelectedBidId ?? report.RecommendedBidId,
            RecommendedPrice = managerApproval.SelectedBid?.TotalPrice ?? report.RecommendedBid?.TotalPrice,
            RecommendedCurrency = managerApproval.SelectedBid?.Currency ?? report.RecommendedBid?.Currency,
            ManagerName = managerApproval.DecidedByUser.FullName,
            ManagerComment = managerApproval.Comment,
            ManagerNote = ExtractManagerNote(managerApproval.Comment),
            ManagerApprovedAt = managerApproval.DecidedAt,
            RequestedItems = tender.Requisition.PurchaseRequisitionItems
                .OrderBy(item => item.Id)
                .Select(item => new TopCommitteeRequestItemViewModel
                {
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    Specifications = item.Specifications
                })
                .ToList(),
            Bids = tender.Bids
                .OrderBy(bid => bid.TotalPrice)
                .Select(bid => new TopCommitteeBidReviewViewModel
                {
                    Id = bid.Id,
                    SupplierName = bid.SupplierProfile.CompanyName,
                    Status = bid.Status,
                    TotalPrice = bid.TotalPrice,
                    Currency = bid.Currency,
                    DeliveryDays = bid.DeliveryDays,
                    ValidityDays = bid.ValidityDays,
                    PaymentTerms = bid.PaymentTerms,
                    SupplierSummary = bid.TechnicalSummary,
                    SubmittedAt = bid.SubmittedAt,
                    Items = bid.BidItems.Select(bidItem => new TopCommitteeBidItemViewModel
                    {
                        ItemName = bidItem.RequisitionItem.ItemName,
                        Quantity = bidItem.Quantity,
                        Unit = bidItem.RequisitionItem.Unit,
                        UnitPrice = bidItem.UnitPrice,
                        TaxAmount = bidItem.TaxAmount,
                        ShippingCost = bidItem.ShippingCost,
                        InstallationCost = bidItem.InstallationCost,
                        OfferedSpecifications = bidItem.OfferedSpecifications
                    }).ToList(),
                    TechnicalScores = bid.TechnicalEvaluations.Select(score => new TopCommitteeTechnicalScoreViewModel
                    {
                        CriterionName = score.Criterion.Name,
                        WeightPercent = score.Criterion.WeightPercent,
                        Score = score.Score,
                        MaxScore = score.Criterion.MaxScore,
                        Notes = score.Notes
                    }).ToList(),
                    FinancialScores = bid.FinancialEvaluations.Select(score => new TopCommitteeFinancialScoreViewModel
                    {
                        Score = score.Score,
                        TotalCost = score.TotalCost,
                        TaxesAndFees = score.TaxesAndFees,
                        LogisticsCost = score.LogisticsCost,
                        Notes = score.Notes
                    }).ToList(),
                    ComplianceResults = bid.ComplianceChecks
                        .Select(check => string.IsNullOrWhiteSpace(check.Reason)
                            ? check.Result
                            : $"{check.Result}: {check.Reason}")
                        .ToList(),
                    Documents = bid.BidDocuments
                        .OrderBy(document => document.FileName)
                        .Select(document => $"{document.DocType}: {document.FileName}")
                        .ToList()
                })
                .ToList(),
            AiAnalyses = tender.AiBidAnalyses
                .OrderByDescending(analysis => analysis.CreatedAt)
                .Select(analysis => new TopCommitteeAiAnalysisViewModel
                {
                    SupplierName = analysis.Bid.SupplierProfile.CompanyName,
                    Model = analysis.Model,
                    ExtractedPrice = analysis.ExtractedPrice,
                    ExtractedDeliveryDays = analysis.ExtractedDeliveryDays,
                    MissingFiles = analysis.MissingFiles,
                    Strengths = analysis.Strengths,
                    Weaknesses = analysis.Weaknesses,
                    Recommendation = analysis.AiRecommendation,
                    CreatedAt = analysis.CreatedAt
                })
                .ToList()
        };

        foreach (var bid in model.Bids)
        {
            var technicalScores = bid.TechnicalScores
                .Where(score => score.Score.HasValue && score.MaxScore > 0)
                .ToList();
            var financialScores = bid.FinancialScores
                .Where(score => score.Score.HasValue)
                .Select(score => score.Score!.Value)
                .ToList();

            if (technicalScores.Count > 0)
            {
                // All four committee members must be Ready before the report reaches this page.
                bid.TechnicalResult = technicalScores.Sum(score =>
                    score.Score!.Value / score.MaxScore * score.WeightPercent) / 4m;
            }
            if (financialScores.Count > 0)
                bid.FinancialResult = financialScores.Average();
            if (bid.TechnicalResult.HasValue && bid.FinancialResult.HasValue)
                bid.TotalResult = bid.TechnicalResult.Value + bid.FinancialResult.Value;
        }

        return View("Index", model);
    }

    private static string ExtractManagerNote(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return string.Empty;
        const string marker = "Manager note:";
        var markerIndex = comment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0
            ? comment.Trim()
            : comment[(markerIndex + marker.Length)..].Trim();
    }

    private IQueryable<Tender> PendingPackages() => _db.Tenders
        .AsNoTracking()
        .Where(tender => tender.RequiresTopCommittee
            && tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
            && tender.EvaluationReports.Any(report => report.Status == Statuses.ReportStatus.ManagerApproved)
            && tender.TenderApprovals.Any(approval => approval.Level == Statuses.ApprovalLevel.Manager
                && approval.Decision == Statuses.ApprovalDecision.Approved));
}
