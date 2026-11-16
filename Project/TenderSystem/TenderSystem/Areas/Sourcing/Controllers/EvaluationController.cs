using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public partial class EvaluationController : Controller
{
    private static readonly string[] ActiveTenderStatuses =
    [
        Statuses.TenderStatus.UnderEvaluation,
        Statuses.TenderStatus.AwaitingManagerReview,
        Statuses.TenderStatus.AwaitingCommitteeDecision
    ];

    private static readonly string[] SubmittedBidStatuses =
    [
        Statuses.BidStatus.Submitted,
        Statuses.BidStatus.UnderReview,
        Statuses.BidStatus.Compliant,
        Statuses.BidStatus.NonCompliant,
        Statuses.BidStatus.Accepted,
        Statuses.BidStatus.Rejected
    ];

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;

    public EvaluationController(ApplicationDbContext db, AuditService audit, NotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int? tenderId,
        TrackingFilters? filters,
        CancellationToken cancellationToken)
    {
        filters ??= new TrackingFilters();
        filters.SupplierSearch = filters.SupplierSearch?.Trim();

        var tenders = await _db.Tenders.AsNoTracking()
            .Where(tender => ActiveTenderStatuses.Contains(tender.Status)
                || tender.Bids.Any(bid => bid.SubmittedAt.HasValue
                    && SubmittedBidStatuses.Contains(bid.Status)
                    && (bid.TechnicalEvaluations.Any() || bid.FinancialEvaluations.Any()))
                || tender.EvaluationReports.Any())
            .LatestActivityFirst(_db)
            .Select(tender => new EvaluationTrackingViewModel.TenderOption(
                tender.Id,
                tender.TenderNumber,
                tender.Title,
                tender.Status,
                tender.SubmissionDeadline,
                tender.Bids.Count(bid => bid.SubmittedAt.HasValue && SubmittedBidStatuses.Contains(bid.Status)),
                tender.EvaluationReports.OrderByDescending(report => report.CreatedAt)
                    .ThenByDescending(report => report.Id)
                    .Select(report => report.Status)
                    .FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);

        var statistics = await BuildStatisticsAsync(cancellationToken);
        var model = new EvaluationTrackingViewModel
        {
            Tenders = tenders,
            SelectedTenderId = tenderId,
            Filters = filters,
            Statistics = statistics
        };

        if (!tenderId.HasValue)
            return View(model with { Notice = tenders.Count == 0 ? "No tenders currently have evaluation activity." : null });

        var tender = await _db.Tenders.AsNoTracking()
            .Where(row => row.Id == tenderId.Value)
            .Select(row => new EvaluationTrackingViewModel.TenderDetails(
                row.Id,
                row.TenderNumber,
                row.Title,
                row.Status,
                row.SubmissionDeadline,
                row.Bids.Count(bid => bid.SubmittedAt.HasValue && SubmittedBidStatuses.Contains(bid.Status)),
                row.Bids.Count(bid => bid.SubmittedAt.HasValue
                    && bid.Status != Statuses.BidStatus.NonCompliant
                    && bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                        .ThenByDescending(check => check.Id)
                        .Select(check => check.Result)
                        .FirstOrDefault() == Statuses.ComplianceResult.Compliant),
                row.EvaluationCriteria.Count(criterion => criterion.Type == Statuses.CriterionType.Technical),
                row.EvaluationCriteria.Count(criterion => criterion.Type == Statuses.CriterionType.Financial),
                row.EvaluationCriteria.Where(criterion => criterion.Type == Statuses.CriterionType.Technical).Sum(criterion => criterion.WeightPercent),
                row.EvaluationCriteria.Where(criterion => criterion.Type == Statuses.CriterionType.Financial).Sum(criterion => criterion.WeightPercent),
                row.AwardedSupplierId,
                row.AwardedSupplier == null ? null : row.AwardedSupplier.CompanyName))
            .SingleOrDefaultAsync(cancellationToken);

        if (tender is null)
            return View(model with { Notice = "This tender was not found. Select a tender from the list." });

        var bids = await _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tender.Id && bid.SubmittedAt.HasValue
                && SubmittedBidStatuses.Contains(bid.Status))
            .OrderByActivity(_db)
            .Select(bid => new
            {
                bid.Id,
                bid.SupplierProfileId,
                SupplierName = bid.SupplierProfile.CompanyName,
                SubmittedAt = bid.SubmittedAt!.Value,
                bid.Status,
                LatestCompliance = bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                    .ThenByDescending(check => check.Id)
                    .Select(check => check.Result)
                    .FirstOrDefault(),
                LatestComplianceReason = bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                    .ThenByDescending(check => check.Id)
                    .Select(check => check.Reason)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var bidIds = bids.Select(bid => bid.Id).ToList();
        var technicalCriteria = await _db.EvaluationCriteria.AsNoTracking()
            .Where(criterion => criterion.TenderId == tender.Id && criterion.Type == Statuses.CriterionType.Technical)
            .Select(criterion => new { criterion.Id, criterion.Name, criterion.MaxScore, criterion.WeightPercent })
            .ToListAsync(cancellationToken);
        var technicalWeight = technicalCriteria.Sum(criterion => criterion.WeightPercent);
        var financialWeight = await _db.EvaluationCriteria.AsNoTracking()
            .Where(criterion => criterion.TenderId == tender.Id && criterion.Type == Statuses.CriterionType.Financial)
            .SumAsync(criterion => (decimal?)criterion.WeightPercent, cancellationToken) ?? 0m;

        var technicalRecords = bidIds.Count == 0
            ? []
            : await _db.TechnicalEvaluations.AsNoTracking()
                .Where(evaluation => bidIds.Contains(evaluation.BidId) && evaluation.Score != null) // a draft note without a score is not progress
                .Select(evaluation => new
                {
                    evaluation.BidId,
                    evaluation.CriterionId,
                    evaluation.EvaluatorUserId,
                    EvaluatorName = evaluation.EvaluatorUser.FullName,
                    evaluation.Score,
                    evaluation.Notes,
                    evaluation.EvaluatedAt,
                    CriterionName = evaluation.Criterion.Name
                })
                .ToListAsync(cancellationToken);

        var financialRecords = bidIds.Count == 0
            ? []
            : await _db.FinancialEvaluations.AsNoTracking()
                .Where(evaluation => bidIds.Contains(evaluation.BidId) && evaluation.Score != null) // pending financial points are not progress
                .Select(evaluation => new
                {
                    evaluation.BidId,
                    evaluation.EvaluatorUserId,
                    EvaluatorName = evaluation.EvaluatorUser.FullName,
                    evaluation.Score,
                    evaluation.Notes,
                    evaluation.EvaluatedAt,
                    evaluation.ReadyAt
                })
                .ToListAsync(cancellationToken);

        var reports = await _db.EvaluationReports.AsNoTracking()
            .Where(report => report.TenderId == tender.Id)
            .OrderByDescending(report => report.CreatedAt)
            .ThenByDescending(report => report.Id)
            .Select(report => new
            {
                report.Id,
                report.Status,
                report.CreatedAt,
                report.SubmittedAt,
                PreparedBy = report.PreparedByUser.FullName,
                RecommendedSupplier = report.RecommendedBid == null ? null : report.RecommendedBid.SupplierProfile.CompanyName,
                RecommendedBidId = report.RecommendedBidId,
                RecommendedPrice = report.RecommendedBid == null ? (decimal?)null : report.RecommendedBid.TotalPrice,
                RecommendedCurrency = report.RecommendedBid == null ? null : report.RecommendedBid.Currency,
                RecommendedDeliveryDays = report.RecommendedBid == null ? (int?)null : report.RecommendedBid.DeliveryDays,
                RecommendedValidityDays = report.RecommendedBid == null ? (int?)null : report.RecommendedBid.ValidityDays,
                RecommendedPaymentTerms = report.RecommendedBid == null ? null : report.RecommendedBid.PaymentTerms,
                report.TechnicalSummary,
                report.FinancialSummary,
                report.Justification
            })
            .ToListAsync(cancellationToken);

        var reportIds = reports.Select(report => report.Id).ToList();
        var approvals = reportIds.Count == 0
            ? []
            : await _db.TenderApprovals.AsNoTracking()
                .Where(approval => reportIds.Contains(approval.ReportId))
                .OrderByDescending(approval => approval.DecidedAt)
                .Select(approval => new
                {
                    approval.ReportId,
                    approval.Level,
                    approval.Decision,
                    approval.DecidedAt,
                    approval.Comment,
                    approval.SelectedBidId,
                    approval.DecidedByUserId,
                    DecidedBy = approval.DecidedByUser.FullName
                })
                .ToListAsync(cancellationToken);

        var latestReport = reports.FirstOrDefault();
        var latestManagerApproval = latestReport is null
            ? null
            : approvals.FirstOrDefault(approval => approval.ReportId == latestReport.Id
                && approval.Level == Statuses.ApprovalLevel.Manager);

        var committeeEntities = await _db.CommitteeMembers.AsNoTracking()
            .Where(member => member.TenderId == tender.Id)
            .OrderBy(member => member.MemberRole)
            .Select(member => new { member.UserId, Name = member.User.FullName, member.MemberRole })
            .ToListAsync(cancellationToken);
        var committeeMemberIds = committeeEntities.Select(member => member.UserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requiredCommitteeMemberCount = committeeMemberIds.Count;

        var eligibleBidIds = bids.Where(bid => (bid.Status == Statuses.BidStatus.Compliant
                    || bid.Status == Statuses.BidStatus.Accepted)
                && bid.LatestCompliance == Statuses.ComplianceResult.Compliant)
            .Select(bid => bid.Id)
            .ToHashSet();

        var bidProgress = bids.Select(bid =>
        {
            var bidTechnical = technicalRecords.Where(record => record.BidId == bid.Id).ToList();
            var completeTechnicalCriteria = bidTechnical.Select(record => record.CriterionId).Distinct().Count();
            var hasTechnical = completeTechnicalCriteria > 0;
            var memberTechnicalPoints = committeeMemberIds.Select(memberId =>
            {
                var memberScores = bidTechnical.Where(record => record.EvaluatorUserId == memberId).ToList();
                if (technicalCriteria.Count == 0 || technicalCriteria.Any(criterion => criterion.MaxScore <= 0
                    || !memberScores.Any(record => record.CriterionId == criterion.Id && record.Score.HasValue)))
                {
                    return (decimal?)null;
                }

                return technicalCriteria.Sum(criterion =>
                {
                    var score = memberScores.Single(record => record.CriterionId == criterion.Id).Score!.Value;
                    return score / criterion.MaxScore * criterion.WeightPercent;
                });
            }).ToList();
            var technicalCompletedMembers = memberTechnicalPoints.Count(points => points.HasValue);
            var isTechnicalComplete = requiredCommitteeMemberCount > 0
                && technicalCriteria.Count > 0
                && technicalCompletedMembers == requiredCommitteeMemberCount;
            var technicalAveragePoints = isTechnicalComplete ? memberTechnicalPoints.Average(points => points!.Value) : (decimal?)null;
            var technicalPercentage = technicalAveragePoints.HasValue && technicalWeight > 0
                ? technicalAveragePoints.Value / technicalWeight * 100m
                : (decimal?)null;
            var bidFinancial = financialRecords.Where(record => record.BidId == bid.Id).ToList();
            var hasFinancial = bidFinancial.Count > 0;
            var financialCompletedMembers = committeeMemberIds.Count(memberId => bidFinancial.Any(record =>
                record.EvaluatorUserId == memberId && record.Score.HasValue));
            var financialReadyMembers = committeeMemberIds.Count(memberId => bidFinancial.Any(record =>
                record.EvaluatorUserId == memberId && record.Score.HasValue && record.ReadyAt.HasValue));
            var isFinancialComplete = requiredCommitteeMemberCount > 0
                && financialCompletedMembers == requiredCommitteeMemberCount;
            var financialAveragePoints = isFinancialComplete
                ? bidFinancial.Where(record => committeeMemberIds.Contains(record.EvaluatorUserId, StringComparer.Ordinal))
                    .Average(record => record.Score!.Value)
                : (decimal?)null;
            var financialPercentage = financialAveragePoints.HasValue && financialWeight > 0
                ? financialAveragePoints.Value / financialWeight * 100m
                : (decimal?)null;
            var isEligible = eligibleBidIds.Contains(bid.Id);
            var technicalStatus = !isEligible ? "Awaiting compliance"
                : technicalCriteria.Count == 0 ? "No technical criteria"
                : isTechnicalComplete ? "Completed"
                : hasTechnical ? "In Progress" : "Pending";
            var financialStatus = !isEligible ? "Awaiting compliance"
                : isFinancialComplete ? "Completed"
                : hasFinancial ? "In Progress" : "Pending";
            var overallStatus = !isEligible ? "Awaiting compliance"
                : isTechnicalComplete && isFinancialComplete ? "Completed"
                : hasTechnical || hasFinancial ? "In Progress" : "Pending";
            var latestBidReport = latestReport;

            var notes = bidTechnical.Where(record => !string.IsNullOrWhiteSpace(record.Notes))
                .Select(record => $"{record.CriterionName}: {record.Notes}")
                .Concat(bidFinancial.Where(record => !string.IsNullOrWhiteSpace(record.Notes))
                    .Select(record => $"Financial: {record.Notes}"))
                .ToList();

            var rejectionReasons = new List<string>();
            if (bid.LatestCompliance == Statuses.ComplianceResult.NonCompliant
                && !string.IsNullOrWhiteSpace(bid.LatestComplianceReason))
            {
                rejectionReasons.Add($"Compliance: {bid.LatestComplianceReason}");
            }

            rejectionReasons.AddRange(approvals
                .Where(approval => approval.SelectedBidId == bid.Id
                    && approval.Decision == Statuses.ApprovalDecision.Rejected
                    && !string.IsNullOrWhiteSpace(approval.Comment))
                .Select(approval => $"{approval.Level} decision: {approval.Comment}"));

            var isRejected = bid.Status == Statuses.BidStatus.Rejected
                || bid.LatestCompliance == Statuses.ComplianceResult.NonCompliant;
            var rejectionReason = rejectionReasons.Count > 0
                ? string.Join(" · ", rejectionReasons)
                : isRejected ? "No rejection reason was recorded." : string.Empty;
            var offerOutcome = tender.Status == Statuses.TenderStatus.Awarded && tender.AwardedSupplierId.HasValue
                ? bid.SupplierProfileId == tender.AwardedSupplierId.Value ? "Awarded" : "Not selected"
                : tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                    && latestManagerApproval?.SelectedBidId == bid.Id
                    ? "Manager recommendation"
                    : tender.Status == Statuses.TenderStatus.Cancelled || tender.Status == Statuses.TenderStatus.Rejected
                        ? "No award"
                : "Not decided";

            return new EvaluationTrackingViewModel.BidProgress(
                bid.Id,
                bid.SupplierName,
                bid.SubmittedAt,
                bid.Status,
                bid.LatestCompliance,
                isEligible,
                technicalStatus,
                completeTechnicalCriteria,
                technicalCriteria.Count,
                technicalCompletedMembers,
                requiredCommitteeMemberCount,
                technicalAveragePoints,
                technicalPercentage,
                bidTechnical.Count == 0 ? null : bidTechnical.Max(record => record.EvaluatedAt),
                financialStatus,
                hasFinancial,
                financialReadyMembers,
                financialAveragePoints,
                financialPercentage,
                bidFinancial.Count == 0 ? null : bidFinancial.Max(record => record.EvaluatedAt),
                overallStatus,
                latestBidReport is null ? "Not Started" : GetReportLabel(latestBidReport.Status, latestBidReport.SubmittedAt, tender.Status),
                !isEligible ? "A latest Compliant result is required; bids marked Non-Compliant are excluded." : string.Join(" · ", notes),
                rejectionReason,
                offerOutcome);
        }).ToList();

        var allBidProgress = bidProgress;
        var readyConfirmationsRequired = eligibleBidIds.Count * requiredCommitteeMemberCount;
        var readyConfirmationsCompleted = financialRecords.Count(record =>
            eligibleBidIds.Contains(record.BidId)
            && committeeMemberIds.Contains(record.EvaluatorUserId, StringComparer.Ordinal)
            && record.ReadyAt.HasValue);
        string? reportReadinessMessage = null;
        if (latestReport is null)
        {
            var requiredCommitteeRoles = new[]
            {
                Statuses.CommitteeRole.Department,
                Statuses.CommitteeRole.Financial,
                Statuses.CommitteeRole.Technical,
                Statuses.CommitteeRole.SourcingManager
            };
            var committeeAssignmentsComplete = committeeEntities.Count == 4
                && committeeMemberIds.Count == 4
                && requiredCommitteeRoles.All(role => committeeEntities.Count(member => member.MemberRole == role) == 1);
            var hasUnreviewedSubmittedBids = bids.Any(bid =>
                bid.Status == Statuses.BidStatus.Submitted
                || bid.Status == Statuses.BidStatus.UnderReview);

            reportReadinessMessage = eligibleBidIds.Count == 0
                ? "The report needs at least one bid with a latest Compliant decision."
                : !committeeAssignmentsComplete
                    ? "The report needs four assigned committee members: one Department, Financial, Technical, and Sourcing Manager representative."
                    : readyConfirmationsCompleted < readyConfirmationsRequired
                        ? $"The report is created after all four committee members select Ready for every eligible bid. Confirmations recorded: {readyConfirmationsCompleted} of {readyConfirmationsRequired}."
                        : hasUnreviewedSubmittedBids
                            ? "Some submitted bids are still awaiting review. Finish their compliance decisions before the committee report can be created."
                            : "All Ready confirmations are recorded, but no report is saved yet. The evaluation team should check that the tender criteria are locked and the eligible offers have comparable, valid prices.";
        }

        var filterOptions = new[] { "Pending", "In Progress", "Completed", "Awaiting compliance" };
        var availableSuppliers = bidProgress.Select(row => row.SupplierName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(filters.SupplierSearch))
            bidProgress = bidProgress.Where(row => row.SupplierName.Contains(filters.SupplierSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(filters.EvaluationStatus) && filterOptions.Contains(filters.EvaluationStatus, StringComparer.Ordinal))
            bidProgress = bidProgress.Where(row => row.OverallStatus == filters.EvaluationStatus).ToList();
        if (!string.IsNullOrWhiteSpace(filters.ReportStatus))
            bidProgress = bidProgress.Where(row => row.ReportStatus == filters.ReportStatus).ToList();
        if (!string.IsNullOrWhiteSpace(filters.CommitteeMemberId))
        {
            var memberRole = committeeEntities.FirstOrDefault(member => member.UserId == filters.CommitteeMemberId)?.MemberRole;
            bidProgress = memberRole switch
            {
                Statuses.CommitteeRole.Technical or Statuses.CommitteeRole.Financial or Statuses.CommitteeRole.SourcingManager
                    => bidProgress.Where(row => row.IsEligible).ToList(),
                Statuses.CommitteeRole.Department => bidProgress.Where(row => row.IsEligible).ToList(),
                _ => []
            };
        }

        var completedTechnicalBids = allBidProgress.Count(row => row.IsEligible && row.TechnicalStatus == "Completed");
        var completedFinancialBids = allBidProgress.Count(row => row.IsEligible && row.FinancialStatus == "Completed");
        var eligibleCount = allBidProgress.Count(row => row.IsEligible);
        var completedOverall = allBidProgress.Count(row => row.IsEligible && row.OverallStatus == "Completed");
        var eligibleTechnicalRecordCount = technicalRecords.Count(record => eligibleBidIds.Contains(record.BidId));
        var eligibleFinancialRecordCount = financialRecords.Count(record => eligibleBidIds.Contains(record.BidId));
        var stages = new List<EvaluationTrackingViewModel.StageProgress>
        {
            new("Technical evaluation", "Technical Representative", GetStageStatus(completedTechnicalBids, eligibleCount), completedTechnicalBids, eligibleCount,
                technicalRecords.Count == 0 ? null : technicalRecords.Max(record => (DateTime?)record.EvaluatedAt),
                $"All assigned members must score each of the {technicalCriteria.Count} technical criteria. Saved member scores: {eligibleTechnicalRecordCount} of {eligibleCount * technicalCriteria.Count * requiredCommitteeMemberCount} required."),
            new("Financial evaluation", "Financial Representative", GetStageStatus(completedFinancialBids, eligibleCount), completedFinancialBids, eligibleCount,
                financialRecords.Count == 0 ? null : financialRecords.Max(record => (DateTime?)record.EvaluatedAt),
                $"Financial scores saved: {eligibleFinancialRecordCount} of {eligibleCount * requiredCommitteeMemberCount} member confirmations. Final Ready: {readyConfirmationsCompleted} of {readyConfirmationsRequired}."),
            new("Unified evaluation report", "Evaluation Committee", latestReport is null ? "Not Started" : latestReport.SubmittedAt.HasValue ? "Sent" : "Ready",
                latestReport is null ? 0 : 1, 1, latestReport?.SubmittedAt ?? latestReport?.CreatedAt,
                latestReport is null ? reportReadinessMessage! : $"Prepared by {latestReport.PreparedBy}; report #{latestReport.Id}."),
            new("Sourcing Manager review", "Sourcing Manager", GetTrackingManagerDecision(latestManagerApproval?.Decision),
                latestManagerApproval is null ? 0 : 1, latestReport?.SubmittedAt is null ? 0 : 1,
                latestManagerApproval?.DecidedAt ?? latestReport?.SubmittedAt,
                latestManagerApproval is null ? latestReport?.SubmittedAt is null ? "Report has not been submitted for manager review." : tender.Status == Statuses.TenderStatus.AwaitingManagerReview ? "The report is in the Sourcing Manager review queue." : "The completed report is ready to send to the Sourcing Manager."
                    : $"{latestManagerApproval.Decision} by {latestManagerApproval.DecidedBy}. {latestManagerApproval.Comment}"),
            new("Overall eligible-bid evaluation", "Technical and Financial Representatives", GetStageStatus(completedOverall, eligibleCount), completedOverall, eligibleCount,
                new[] { technicalRecords.Count == 0 ? (DateTime?)null : technicalRecords.Max(record => (DateTime?)record.EvaluatedAt), financialRecords.Count == 0 ? (DateTime?)null : financialRecords.Max(record => (DateTime?)record.EvaluatedAt) }.Max(),
                "A bid is complete when all technical criteria have a score and a financial evaluation record exists.")
        };

        var committeeProgress = committeeEntities.Select(member =>
        {
            if (member.MemberRole == Statuses.CommitteeRole.Technical)
            {
                var records = technicalRecords.Where(record => record.EvaluatorUserId == member.UserId).ToList();
                var assignedEligibleIds = bids.Where(bid => eligibleBidIds.Contains(bid.Id)).Select(bid => bid.Id).ToHashSet();
                var memberCriterionCount = records.Where(record => assignedEligibleIds.Contains(record.BidId))
                    .Select(record => (record.BidId, record.CriterionId)).Distinct().Count();
                var expectedCriterionCount = assignedEligibleIds.Count * technicalCriteria.Count;
                var activity = assignedEligibleIds.Count == 0 ? "No eligible offers to evaluate."
                    : technicalCriteria.Count == 0 ? "No technical criteria are defined."
                    : $"Scored {memberCriterionCount} of {expectedCriterionCount} technical criteria across eligible offers.";
                return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name,
                    GetCommitteeRoleLabel(member.MemberRole), activity);
            }
            if (member.MemberRole == Statuses.CommitteeRole.Financial)
            {
                var records = financialRecords.Where(record => record.EvaluatorUserId == member.UserId).ToList();
                var assignedEligibleIds = bids.Where(bid => eligibleBidIds.Contains(bid.Id)).Select(bid => bid.Id).ToHashSet();
                var memberCompletedBidCount = records.Select(record => record.BidId).Distinct().Count(assignedEligibleIds.Contains);
                var activity = assignedEligibleIds.Count == 0 ? "No eligible offers to evaluate."
                    : $"Evaluated {memberCompletedBidCount} of {assignedEligibleIds.Count} eligible offers.";
                return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name,
                    GetCommitteeRoleLabel(member.MemberRole), activity);
            }
            // Every committee member evaluates the same points. The Department Manager and the Department Financial Officer
            // (seat code SourcingManager) are both shown with their technical scores and Ready confirmations.
            if (member.MemberRole is Statuses.CommitteeRole.Department or Statuses.CommitteeRole.SourcingManager)
            {
                var eligibleIds = eligibleBidIds;
                var memberTechnicalRecords = technicalRecords.Where(record => record.EvaluatorUserId == member.UserId
                    && eligibleIds.Contains(record.BidId)).ToList();
                var completedCriteria = memberTechnicalRecords
                    .Select(record => (record.BidId, record.CriterionId)).Distinct().Count();
                var requiredCriteria = eligibleIds.Count * technicalCriteria.Count;
                var memberReadyCount = financialRecords.Count(record => record.EvaluatorUserId == member.UserId
                    && eligibleIds.Contains(record.BidId) && record.ReadyAt.HasValue);
                var activity = eligibleIds.Count == 0
                    ? "No eligible offers to confirm."
                    : $"Scored {completedCriteria} of {requiredCriteria} technical criteria; Ready confirmed for {memberReadyCount} of {eligibleIds.Count} eligible offers.";
                return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name,
                    GetCommitteeRoleLabel(member.MemberRole), activity);
            }

            return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name,
                GetCommitteeRoleLabel(member.MemberRole), "Department review is not recorded separately.");
        }).ToList();

        var clarificationThreadCount = await _db.Clarifications.AsNoTracking()
            .CountAsync(clarification => clarification.TenderId == tender.Id
                || (clarification.BidId.HasValue && clarification.Bid != null && clarification.Bid.TenderId == tender.Id),
                cancellationToken);

        var latestReportDetails = latestReport is null ? null : new EvaluationTrackingViewModel.EvaluationReportDetails(
            latestReport.Id,
            latestReport.Status,
            GetReportLabel(latestReport.Status, latestReport.SubmittedAt, tender.Status),
            latestReport.CreatedAt,
            latestReport.SubmittedAt,
            latestReport.PreparedBy,
            latestReport.RecommendedSupplier,
            latestReport.RecommendedBidId,
            latestReport.RecommendedPrice,
            latestReport.RecommendedCurrency,
            latestReport.RecommendedDeliveryDays,
            latestReport.RecommendedValidityDays,
            latestReport.RecommendedPaymentTerms,
            latestReport.TechnicalSummary,
            latestReport.FinancialSummary,
            latestReport.Justification,
            latestManagerApproval?.Decision ?? (latestReport.SubmittedAt.HasValue
                ? tender.Status == Statuses.TenderStatus.AwaitingManagerReview ? "Awaiting manager review" : "Ready to send to manager"
                : "Not submitted"),
            latestManagerApproval?.DecidedAt,
            latestManagerApproval?.Comment,
            latestManagerApproval?.SelectedBidId is int selectedBidId
                ? bids.FirstOrDefault(bid => bid.Id == selectedBidId)?.SupplierName
                : null);

        var canManageSupplierResults = tender.Status == Statuses.TenderStatus.Awarded
            && tender.AwardedSupplierId.HasValue
            && await _db.Tenders.AnyAsync(row => row.Id == tender.Id
                && row.CreatedByUserId == User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);

        return View(model with
        {
            SelectedTender = tender,
            CanManageSupplierResults = canManageSupplierResults,
            SupplierResultsSent = canManageSupplierResults
                && await SupplierResultsSentAsync(tender.Id, cancellationToken),
            MatchingBidCount = bidProgress.Count,
            AvailableSuppliers = availableSuppliers,
            Bids = bidProgress,
            Stages = stages,
            CommitteeMembers = committeeProgress,
            LatestReport = latestReportDetails,
            ReadyConfirmationsCompleted = readyConfirmationsCompleted,
            ReadyConfirmationsRequired = readyConfirmationsRequired,
            ReportReadinessMessage = reportReadinessMessage,
            CanSendReportToManager = latestReport is not null
                && latestReport.Status == Statuses.ReportStatus.RecommendationPending
                && latestReport.SubmittedAt.HasValue
                && tender.Status == Statuses.TenderStatus.UnderEvaluation,
            ClarificationThreadCount = clarificationThreadCount,
            Notice = bids.Count == 0 ? "This tender has no submitted bids to track." : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToManager(int tenderId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var tender = await _db.Tenders.SingleOrDefaultAsync(row => row.Id == tenderId, cancellationToken);
        if (tender is null) return NotFound();

        if (tender.Status == Statuses.TenderStatus.AwaitingManagerReview)
        {
            TempData["EvaluationTrackingMessage"] = "This report is already in the Sourcing Manager review queue.";
            return RedirectToAction(nameof(Index), new { tenderId });
        }

        if (tender.Status != Statuses.TenderStatus.UnderEvaluation)
        {
            TempData["EvaluationTrackingError"] = "This tender is not at the evaluation handoff stage.";
            return RedirectToAction(nameof(Index), new { tenderId });
        }

        var report = await _db.EvaluationReports
            .Where(row => row.TenderId == tenderId)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (report is null
            || report.Status != Statuses.ReportStatus.RecommendationPending
            || !report.SubmittedAt.HasValue)
        {
            TempData["EvaluationTrackingError"] = "The committee has not completed and submitted a unified evaluation report yet.";
            return RedirectToAction(nameof(Index), new { tenderId });
        }

        var previousStatus = tender.Status;
        tender.Status = Statuses.TenderStatus.AwaitingManagerReview;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "SubmittedToManagerReview",
            "EvaluationReport",
            report.Id.ToString(),
            $"Report for {tender.TenderNumber} moved to the assigned Sourcing Manager review queue.",
            previousStatus);

        var managerReviewLink = Url.Action("Index", "Reports", new { area = "Manager", id = tenderId })
            ?? "/Manager/Reports";
        try
        {
            await _notifications.NotifyRoleAsync(
                Roles.SourcingManager,
                "EvaluationReportForManager",
                "Evaluation report ready for review",
                $"The unified evaluation report for {tender.TenderNumber} is ready, including committee results and offer details.",
                managerReviewLink);
            TempData["EvaluationTrackingMessage"] = "The complete evaluation report was sent to the Sourcing Manager for review.";
        }
        catch
        {
            TempData["EvaluationTrackingMessage"] = "The report is now in the Sourcing Manager review queue. The notification could not be delivered, but the manager can still open it from Reports.";
        }

        return RedirectToAction(nameof(Index), new { tenderId });
    }

    private async Task<EvaluationTrackingViewModel.SummaryStatistics> BuildStatisticsAsync(CancellationToken cancellationToken)
    {
        var activeTenderIds = await _db.Tenders.AsNoTracking()
            .Where(tender => ActiveTenderStatuses.Contains(tender.Status))
            .Select(tender => tender.Id)
            .ToListAsync(cancellationToken);

        var evaluationBids = activeTenderIds.Count == 0
            ? []
            : await _db.Bids.AsNoTracking()
                .Where(bid => activeTenderIds.Contains(bid.TenderId) && bid.SubmittedAt.HasValue
                    && SubmittedBidStatuses.Contains(bid.Status))
                .Select(bid => new
                {
                    bid.Id,
                    bid.TenderId,
                    bid.Status,
                    LatestCompliance = bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                        .ThenByDescending(check => check.Id).Select(check => check.Result).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

        var eligibleBids = evaluationBids.Where(bid => (bid.Status == Statuses.BidStatus.Compliant
                    || bid.Status == Statuses.BidStatus.Accepted)
                && bid.LatestCompliance == Statuses.ComplianceResult.Compliant)
            .ToList();
        var eligibleIds = eligibleBids.Select(bid => bid.Id).ToList();
        var requiredTechnicalCounts = activeTenderIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.EvaluationCriteria.AsNoTracking()
                .Where(criterion => activeTenderIds.Contains(criterion.TenderId) && criterion.Type == Statuses.CriterionType.Technical)
                .GroupBy(criterion => criterion.TenderId)
                .Select(group => new { TenderId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.TenderId, row => row.Count, cancellationToken);

        var committeeAssignments = activeTenderIds.Count == 0
            ? []
            : await _db.CommitteeMembers.AsNoTracking()
                .Where(member => activeTenderIds.Contains(member.TenderId))
                .Select(member => new { member.TenderId, member.UserId })
                .ToListAsync(cancellationToken);
        var committeeByTender = committeeAssignments.GroupBy(member => member.TenderId)
            .ToDictionary(group => group.Key, group => group.Select(member => member.UserId)
                .Distinct(StringComparer.Ordinal).ToList());

        var tech = eligibleIds.Count == 0
            ? []
            : await _db.TechnicalEvaluations.AsNoTracking().Where(record => eligibleIds.Contains(record.BidId) && record.Score != null)
                .Select(record => new { record.BidId, record.CriterionId, record.EvaluatorUserId, record.EvaluatedAt }).ToListAsync(cancellationToken);
        var financial = eligibleIds.Count == 0
            ? []
            : await _db.FinancialEvaluations.AsNoTracking().Where(record => eligibleIds.Contains(record.BidId) && record.Score != null)
                .Select(record => new { record.BidId, record.EvaluatorUserId, record.EvaluatedAt }).ToListAsync(cancellationToken);

        var progressByBid = eligibleBids.Select(bid =>
        {
            var memberIds = committeeByTender.GetValueOrDefault(bid.TenderId) ?? [];
            var criterionCount = requiredTechnicalCounts.GetValueOrDefault(bid.TenderId);
            var technicalComplete = memberIds.Count > 0 && criterionCount > 0
                && memberIds.All(memberId =>
                {
                    var memberCriteria = tech.Where(record => record.BidId == bid.Id && record.EvaluatorUserId == memberId)
                        .Select(record => record.CriterionId).ToHashSet();
                    return memberCriteria.Count >= criterionCount;
                });
            var financialComplete = memberIds.Count > 0
                && memberIds.All(memberId => financial.Any(record => record.BidId == bid.Id && record.EvaluatorUserId == memberId));
            var hasProgress = tech.Any(record => record.BidId == bid.Id)
                || financial.Any(record => record.BidId == bid.Id);
            return new { bid.Id, IsComplete = technicalComplete && financialComplete, HasProgress = hasProgress };
        }).ToList();
        var completed = progressByBid.Count(bid => bid.IsComplete);
        var inProgress = progressByBid.Count(bid => !bid.IsComplete && bid.HasProgress);
        var pending = eligibleIds.Count - completed - inProgress;

        var reportsAwaitingManager = await _db.EvaluationReports.AsNoTracking()
            .CountAsync(report => ActiveTenderStatuses.Contains(report.Tender.Status)
                && report.Status == Statuses.ReportStatus.RecommendationPending
                && report.SubmittedAt.HasValue
                && !report.TenderApprovals.Any(approval => approval.ReportId == report.Id
                    && approval.Level == Statuses.ApprovalLevel.Manager), cancellationToken);

        return new EvaluationTrackingViewModel.SummaryStatistics(
            activeTenderIds.Count,
            eligibleIds.Count,
            completed,
            inProgress,
            pending,
            reportsAwaitingManager);
    }

    private static string GetStageStatus(int completed, int total) => total == 0 ? "No eligible bids"
        : completed == 0 ? "Pending" : completed >= total ? "Completed" : "In Progress";

    private static string GetReportLabel(string status, DateTime? submittedAt, string tenderStatus) =>
        status == Statuses.ReportStatus.RecommendationPending && !submittedAt.HasValue
            ? "In Progress"
        : status == Statuses.ReportStatus.RecommendationPending && tenderStatus == Statuses.TenderStatus.AwaitingManagerReview
            ? "Submitted for Manager Review"
        : status == Statuses.ReportStatus.RecommendationPending && tenderStatus == Statuses.TenderStatus.UnderEvaluation
            ? "Ready to Send to Manager"
            : status;

    private static string GetManagerStageStatus(bool reportExists, bool submitted, string tenderStatus, string? decision) =>
        decision is not null ? decision
        : !reportExists ? "Not Started"
        : submitted && tenderStatus == Statuses.TenderStatus.AwaitingManagerReview ? "Pending"
        : submitted && tenderStatus == Statuses.TenderStatus.UnderEvaluation ? "Ready to Send"
        : "Not Submitted";

    private static string GetTrackingManagerDecision(string? decision) => decision switch
    {
        Statuses.ApprovalDecision.Approved => "Approved",
        Statuses.ApprovalDecision.Returned => "Returned",
        _ => "Pending"
    };

    private static string GetCommitteeRoleLabel(string memberRole) => CommitteePositions.TitleFor(memberRole);
}
