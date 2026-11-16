using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models;
using TenderSystem.Areas.Committee.Services;
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
    private readonly CommitteeVotingService _voting;

    public ReviewController(ApplicationDbContext db, CommitteeVotingService voting)
    {
        _db = db;
        _voting = voting;
    }

    public async Task<IActionResult> Index(int? id)
    {
        if (!id.HasValue)
        {
            // The package is the report named by the LATEST Manager decision (same rule as voting).
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
                            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                            .OrderByDescending(approval => approval.DecidedAt)
                            .ThenByDescending(approval => approval.Id)
                            .Select(approval => approval.Report.RecommendedBid == null
                                || approval.Report.RecommendedBid.TenderId != tender.Id
                                    ? string.Empty
                                    : approval.Report.RecommendedBid.SupplierProfile.CompanyName)
                            .FirstOrDefault() ?? string.Empty,
                        RecommendedPrice = tender.TenderApprovals
                            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                            .OrderByDescending(approval => approval.DecidedAt)
                            .ThenByDescending(approval => approval.Id)
                            .Select(approval => approval.Report.RecommendedBid == null
                                || approval.Report.RecommendedBid.TenderId != tender.Id
                                    ? (decimal?)null
                                    : approval.Report.RecommendedBid.TotalPrice)
                            .FirstOrDefault(),
                        RecommendedCurrency = tender.TenderApprovals
                            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                            .OrderByDescending(approval => approval.DecidedAt)
                            .ThenByDescending(approval => approval.Id)
                            .Select(approval => approval.Report.RecommendedBid == null
                                || approval.Report.RecommendedBid.TenderId != tender.Id
                                    ? null
                                    : approval.Report.RecommendedBid.Currency)
                            .FirstOrDefault(),
                        ManagerName = tender.TenderApprovals
                            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                            .OrderByDescending(approval => approval.DecidedAt)
                            .ThenByDescending(approval => approval.Id)
                            .Select(approval => approval.DecidedByUser.FullName)
                            .FirstOrDefault() ?? string.Empty,
                        ManagerComment = tender.TenderApprovals
                            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                            .OrderByDescending(approval => approval.DecidedAt)
                            .ThenByDescending(approval => approval.Id)
                            .Select(approval => approval.Comment)
                            .FirstOrDefault() ?? string.Empty,
                        ManagerApprovedAt = tender.TenderApprovals
                            .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                            .OrderByDescending(approval => approval.DecidedAt)
                            .ThenByDescending(approval => approval.Id)
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
            .Include(item => item.TenderApprovals.Where(approval => approval.Level == Statuses.ApprovalLevel.Manager))
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

        // The latest Manager decision is the forward to the committee; its ReportId names the approved report.
        var managerApproval = tender.TenderApprovals
            .OrderByDescending(approval => approval.DecidedAt)
            .ThenByDescending(approval => approval.Id)
            .FirstOrDefault();
        if (managerApproval == null || managerApproval.Decision != Statuses.ApprovalDecision.Approved)
            return NotFound();

        var report = tender.EvaluationReports.FirstOrDefault(item => item.Id == managerApproval.ReportId);
        if (report == null)
            return NotFound();

        // The recommended bid must be named by the report AND belong to this tender. Never picked automatically.
        var recommendedBid = report.RecommendedBidId is int recommendedBidId
            ? tender.Bids.FirstOrDefault(bid => bid.Id == recommendedBidId)
            : null;
        string? recommendationUnavailable = report.RecommendedBidId is null
            ? "The approved evaluation report does not name a recommended bid. Ask the Sourcing Manager to correct the report before voting."
            : recommendedBid is null
                ? "The recommended bid in the approved report does not belong to this tender. Ask the Sourcing Manager to review the report before voting."
                : null;

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
            RecommendedBidId = recommendedBid?.Id,
            ManagerSelectedBidId = managerApproval.SelectedBidId ?? report.RecommendedBidId,
            RecommendedSupplier = recommendedBid?.SupplierProfile.CompanyName ?? string.Empty,
            RecommendedPrice = recommendedBid?.TotalPrice,
            RecommendedCurrency = recommendedBid?.Currency,
            RecommendationUnavailableReason = recommendationUnavailable,
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
                .OrderByDescending(bid => recommendedBid != null && bid.Id == recommendedBid.Id)
                .ThenBy(bid => bid.TotalPrice)
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

        // Voting availability comes from the existing read-only voting status (the vote itself is re-checked on POST).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var votingStatus = string.IsNullOrWhiteSpace(userId)
            ? null
            : await _voting.GetStatusAsync(tender.Id, userId);
        model.CanProceedToVoting = recommendedBid != null
            && votingStatus != null
            && votingStatus.BlockReason == null;
        model.VotingBlockedReason = model.CanProceedToVoting
            ? null
            : recommendationUnavailable
                ?? votingStatus?.BlockReason
                ?? "Voting is not available for this tender.";

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

    // Tenders whose LATEST Manager decision is an approval of a ManagerApproved report of the same tender.
    private IQueryable<Tender> PendingPackages() => _db.Tenders
        .AsNoTracking()
        .Where(tender => tender.RequiresTopCommittee
            && tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
            && tender.TenderApprovals
                .Where(approval => approval.Level == Statuses.ApprovalLevel.Manager)
                .OrderByDescending(approval => approval.DecidedAt)
                .ThenByDescending(approval => approval.Id)
                .Select(approval => approval.Decision == Statuses.ApprovalDecision.Approved
                    && approval.Report.TenderId == tender.Id
                    && approval.Report.Status == Statuses.ReportStatus.ManagerApproved)
                .FirstOrDefault());
}
