using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class EvaluationController : Controller
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

    public EvaluationController(ApplicationDbContext db) => _db = db;

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
            .OrderByDescending(tender => tender.SubmissionDeadline)
            .ThenByDescending(tender => tender.Id)
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
                row.EvaluationCriteria.Count(criterion => criterion.Type == Statuses.CriterionType.Financial)))
            .SingleOrDefaultAsync(cancellationToken);

        if (tender is null)
            return View(model with { Notice = "This tender was not found. Select a tender from the list." });

        var bids = await _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == tender.Id && bid.SubmittedAt.HasValue
                && SubmittedBidStatuses.Contains(bid.Status))
            .OrderByDescending(bid => bid.SubmittedAt)
            .ThenBy(bid => bid.Id)
            .Select(bid => new
            {
                bid.Id,
                SupplierName = bid.SupplierProfile.CompanyName,
                SubmittedAt = bid.SubmittedAt!.Value,
                bid.Status,
                LatestCompliance = bid.ComplianceChecks.OrderByDescending(check => check.CheckedAt)
                    .ThenByDescending(check => check.Id)
                    .Select(check => check.Result)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var bidIds = bids.Select(bid => bid.Id).ToList();
        var technicalCriteria = await _db.EvaluationCriteria.AsNoTracking()
            .Where(criterion => criterion.TenderId == tender.Id && criterion.Type == Statuses.CriterionType.Technical)
            .Select(criterion => new { criterion.Id, criterion.Name, criterion.MaxScore, criterion.WeightPercent })
            .ToListAsync(cancellationToken);

        var technicalRecords = bidIds.Count == 0
            ? []
            : await _db.TechnicalEvaluations.AsNoTracking()
                .Where(evaluation => bidIds.Contains(evaluation.BidId))
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
                .Where(evaluation => bidIds.Contains(evaluation.BidId))
                .Select(evaluation => new
                {
                    evaluation.BidId,
                    evaluation.EvaluatorUserId,
                    EvaluatorName = evaluation.EvaluatorUser.FullName,
                    evaluation.Score,
                    evaluation.Notes,
                    evaluation.EvaluatedAt
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

        var eligibleBidIds = bids.Where(bid => bid.Status != Statuses.BidStatus.NonCompliant
                && bid.LatestCompliance == Statuses.ComplianceResult.Compliant)
            .Select(bid => bid.Id)
            .ToHashSet();

        var bidProgress = bids.Select(bid =>
        {
            var bidTechnical = technicalRecords.Where(record => record.BidId == bid.Id).ToList();
            var completeTechnicalCriteria = bidTechnical.Select(record => record.CriterionId).Distinct().Count();
            var hasTechnical = completeTechnicalCriteria > 0;
            var isTechnicalComplete = technicalCriteria.Count > 0
                && technicalCriteria.All(criterion => bidTechnical.Any(record => record.CriterionId == criterion.Id));
            var bidFinancial = financialRecords.Where(record => record.BidId == bid.Id).ToList();
            var hasFinancial = bidFinancial.Count > 0;
            var isEligible = eligibleBidIds.Contains(bid.Id);
            var technicalStatus = !isEligible ? "Awaiting compliance"
                : technicalCriteria.Count == 0 ? "No technical criteria"
                : isTechnicalComplete ? "Completed"
                : hasTechnical ? "In Progress" : "Pending";
            var financialStatus = !isEligible ? "Awaiting compliance"
                : hasFinancial ? "Completed" : "Pending";
            var overallStatus = !isEligible ? "Awaiting compliance"
                : isTechnicalComplete && hasFinancial ? "Completed"
                : hasTechnical || hasFinancial ? "In Progress" : "Pending";
            var latestBidReport = latestReport;

            var notes = bidTechnical.Where(record => !string.IsNullOrWhiteSpace(record.Notes))
                .Select(record => $"{record.CriterionName}: {record.Notes}")
                .Concat(bidFinancial.Where(record => !string.IsNullOrWhiteSpace(record.Notes))
                    .Select(record => $"Financial: {record.Notes}"))
                .ToList();

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
                bidTechnical.Count == 0 ? null : bidTechnical.Max(record => record.EvaluatedAt),
                financialStatus,
                hasFinancial,
                bidFinancial.Count == 0 ? null : bidFinancial.Max(record => record.EvaluatedAt),
                overallStatus,
                latestBidReport is null ? "Not Started" : GetReportLabel(latestBidReport.Status, latestBidReport.SubmittedAt),
                !isEligible ? "A latest Compliant result is required; bids marked Non-Compliant are excluded." : string.Join(" · ", notes));
        }).ToList();

        var filterOptions = new[] { "Pending", "In Progress", "Completed", "Awaiting compliance" };
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
                Statuses.CommitteeRole.Department => [],
                _ => []
            };
        }

        var completedTechnicalBids = bidProgress.Count(row => row.IsEligible && row.TechnicalStatus == "Completed");
        var completedFinancialBids = bidProgress.Count(row => row.IsEligible && row.FinancialStatus == "Completed");
        var eligibleCount = bidProgress.Count(row => row.IsEligible);
        var completedOverall = bidProgress.Count(row => row.IsEligible && row.OverallStatus == "Completed");

        var stages = new List<EvaluationTrackingViewModel.StageProgress>
        {
            new("Technical evaluation", "Technical Representative", GetStageStatus(completedTechnicalBids, eligibleCount), completedTechnicalBids, eligibleCount,
                technicalRecords.Count == 0 ? null : technicalRecords.Max(record => (DateTime?)record.EvaluatedAt),
                $"{technicalCriteria.Count} technical criteria per eligible bid; scores recorded: {technicalRecords.Count}."),
            new("Financial evaluation", "Financial Representative", GetStageStatus(completedFinancialBids, eligibleCount), completedFinancialBids, eligibleCount,
                financialRecords.Count == 0 ? null : financialRecords.Max(record => (DateTime?)record.EvaluatedAt),
                $"Financial evaluation records saved: {financialRecords.Count} of {eligibleCount} eligible bids."),
            new("Department review", "Department Representative", "Not tracked separately", 0, 0, null,
                "The current schema has committee assignment only; it does not store separate department review records."),
            new("Unified evaluation report", "Evaluation Committee", latestReport is null ? "Not Started" : GetReportLabel(latestReport.Status, latestReport.SubmittedAt),
                latestReport is null ? 0 : 1, 1, latestReport?.SubmittedAt ?? latestReport?.CreatedAt,
                latestReport is null ? "No evaluation report exists for this tender." : $"Prepared by {latestReport.PreparedBy}; report #{latestReport.Id}."),
            new("Sourcing Manager review", "Sourcing Manager", GetManagerStageStatus(latestReport is not null, latestReport?.SubmittedAt.HasValue == true, latestManagerApproval?.Decision),
                latestManagerApproval is null ? 0 : 1, latestReport?.SubmittedAt is null ? 0 : 1,
                latestManagerApproval?.DecidedAt ?? latestReport?.SubmittedAt,
                latestManagerApproval is null ? latestReport?.SubmittedAt is null ? "Report has not been submitted for manager review." : "Submitted report is awaiting a recorded manager decision."
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
                return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name, member.MemberRole,
                    $"{records.Select(record => record.BidId).Distinct().Count()} bid(s), {records.Select(record => record.CriterionId).Distinct().Count()} criterion(s) scored",
                    records.Count == 0 ? "Pending" : expectedCriterionCount > 0 && memberCriterionCount >= expectedCriterionCount ? "Completed" : "In Progress",
                    records.Count == 0 ? null : records.Max(record => record.EvaluatedAt),
                    records.Select(record => record.Notes).FirstOrDefault(note => !string.IsNullOrWhiteSpace(note)));
            }
            if (member.MemberRole == Statuses.CommitteeRole.Financial)
            {
                var records = financialRecords.Where(record => record.EvaluatorUserId == member.UserId).ToList();
                var assignedEligibleIds = bids.Where(bid => eligibleBidIds.Contains(bid.Id)).Select(bid => bid.Id).ToHashSet();
                var memberCompletedBidCount = records.Select(record => record.BidId).Distinct().Count(assignedEligibleIds.Contains);
                return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name, member.MemberRole,
                    $"{records.Select(record => record.BidId).Distinct().Count()} bid(s) financially evaluated",
                    records.Count == 0 ? "Pending" : assignedEligibleIds.Count > 0 && memberCompletedBidCount >= assignedEligibleIds.Count ? "Completed" : "In Progress",
                    records.Count == 0 ? null : records.Max(record => record.EvaluatedAt),
                    records.Select(record => record.Notes).FirstOrDefault(note => !string.IsNullOrWhiteSpace(note)));
            }
            if (member.MemberRole == Statuses.CommitteeRole.SourcingManager)
            {
                var approvalsByMember = approvals.Where(approval => approval.DecidedByUserId == member.UserId
                    && approval.ReportId == latestReport?.Id
                    && approval.Level == Statuses.ApprovalLevel.Manager).ToList();
                return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name, member.MemberRole,
                    approvalsByMember.Count == 0 ? "Manager decision is stored by report, not linked to committee assignment." : $"{approvalsByMember.Count} manager decision(s)",
                    approvalsByMember.Count == 0 ? "Pending" : "Completed", approvalsByMember.FirstOrDefault()?.DecidedAt,
                    approvalsByMember.FirstOrDefault()?.Comment);
            }

            return new EvaluationTrackingViewModel.CommitteeProgress(member.UserId, member.Name, member.MemberRole,
                "No separate department review record exists in the current schema.", "No separate task record", null, null);
        }).ToList();

        var latestReportDetails = latestReport is null ? null : new EvaluationTrackingViewModel.EvaluationReportDetails(
            latestReport.Id,
            latestReport.Status,
            GetReportLabel(latestReport.Status, latestReport.SubmittedAt),
            latestReport.CreatedAt,
            latestReport.SubmittedAt,
            latestReport.PreparedBy,
            latestReport.RecommendedSupplier,
            latestReport.TechnicalSummary,
            latestReport.FinancialSummary,
            latestReport.Justification,
            latestManagerApproval?.Decision ?? (latestReport.SubmittedAt.HasValue ? "Awaiting manager review" : "Not submitted"),
            latestManagerApproval?.DecidedAt,
            latestManagerApproval?.Comment);

        return View(model with
        {
            SelectedTender = tender,
            MatchingBidCount = bidProgress.Count,
            Bids = bidProgress,
            Stages = stages,
            CommitteeMembers = committeeProgress,
            LatestReport = latestReportDetails,
            Notice = bids.Count == 0 ? "This tender has no submitted bids to track." : null
        });
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

        var eligibleIds = evaluationBids.Where(bid => bid.Status != Statuses.BidStatus.NonCompliant
                && bid.LatestCompliance == Statuses.ComplianceResult.Compliant)
            .Select(bid => bid.Id).ToList();
        var requiredTechnicalCounts = activeTenderIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.EvaluationCriteria.AsNoTracking()
                .Where(criterion => activeTenderIds.Contains(criterion.TenderId) && criterion.Type == Statuses.CriterionType.Technical)
                .GroupBy(criterion => criterion.TenderId)
                .Select(group => new { TenderId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.TenderId, row => row.Count, cancellationToken);

        var tech = eligibleIds.Count == 0
            ? []
            : await _db.TechnicalEvaluations.AsNoTracking().Where(record => eligibleIds.Contains(record.BidId))
                .Select(record => new { record.BidId, record.CriterionId, record.EvaluatedAt }).ToListAsync(cancellationToken);
        var financial = eligibleIds.Count == 0
            ? []
            : await _db.FinancialEvaluations.AsNoTracking().Where(record => eligibleIds.Contains(record.BidId))
                .Select(record => new { record.BidId, record.EvaluatedAt }).ToListAsync(cancellationToken);

        var completeTechnicalIds = evaluationBids.Where(bid => eligibleIds.Contains(bid.Id))
            .Where(bid => requiredTechnicalCounts.GetValueOrDefault(bid.TenderId) > 0
                && tech.Where(record => record.BidId == bid.Id).Select(record => record.CriterionId).Distinct().Count()
                    >= requiredTechnicalCounts.GetValueOrDefault(bid.TenderId))
            .Select(bid => bid.Id).ToHashSet();
        var financialIds = financial.Select(record => record.BidId).ToHashSet();
        var completed = eligibleIds.Count(id => completeTechnicalIds.Contains(id) && financialIds.Contains(id));
        var inProgress = eligibleIds.Count(id => !(completeTechnicalIds.Contains(id) && financialIds.Contains(id))
            && (tech.Any(record => record.BidId == id) || financialIds.Contains(id)));
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

    private static string GetReportLabel(string status, DateTime? submittedAt) =>
        status == Statuses.ReportStatus.RecommendationPending && submittedAt.HasValue
            ? "Submitted for Manager Review"
        : status == Statuses.ReportStatus.RecommendationPending
            ? "In Progress"
            : status;

    private static string GetManagerStageStatus(bool reportExists, bool submitted, string? decision) =>
        decision is not null ? decision
        : !reportExists ? "Not Started"
        : submitted ? "Pending"
        : "Not Submitted";
}
