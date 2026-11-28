using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Areas.Manager.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;

namespace TenderSystem.Areas.Manager.Controllers;

[Area("Manager")]
[Authorize(Roles = Roles.SourcingManager)]
public class ReportsController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;

    public ReportsController(ApplicationDbContext context, IFileStorageService fileStorage)
    {
        _context = context;
        _fileStorage = fileStorage;
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
                PendingReports = await _context.EvaluationReports
                    .AsNoTracking()
                    .Where(report => report.Status == Statuses.ReportStatus.RecommendationPending
                        && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview)
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

        var model = await _context.EvaluationReports
            .AsNoTracking()
            .Where(report => report.TenderId == id
                && report.Status == Statuses.ReportStatus.RecommendationPending
                && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview)
            .OrderByDescending(report => report.SubmittedAt ?? report.CreatedAt)
            .Select(report => new ManagerReportReviewViewModel
            {
                TenderId = report.TenderId,
                RequisitionId = report.Tender.RequisitionId,
                ReportId = report.Id,
                RecommendedBidId = report.RecommendedBid != null
                    && (report.RecommendedBid.Status == Statuses.BidStatus.Compliant || report.RecommendedBid.Status == Statuses.BidStatus.Accepted)
                    ? report.RecommendedBidId : null,
                TenderNumber = report.Tender.TenderNumber,
                TenderTitle = report.Tender.Title,
                TenderStatus = report.Tender.Status,
                RequestNumber = report.Tender.Requisition.RequestNumber,
                RequestTitle = report.Tender.Requisition.Title,
                DepartmentName = report.Tender.Requisition.Department.Name,
                RequesterName = report.Tender.Requisition.RequestedByUser.FullName,
                RequestReason = report.Tender.Requisition.Reason,
                RequestPriority = report.Tender.Requisition.Priority,
                RequestTargetDate = report.Tender.Requisition.TargetDate,
                TenderDescription = report.Tender.Description,
                TenderTerms = report.Tender.Terms,
                PublishDate = report.Tender.PublishDate,
                SubmissionDeadline = report.Tender.SubmissionDeadline,
                PreparedByName = report.PreparedByUser.FullName,
                EstimatedValue = report.Tender.EstimatedValue,
                RequiresTopCommittee = report.Tender.RequiresTopCommittee,
                ReportStatus = report.Status,
                SubmittedAt = report.SubmittedAt ?? report.CreatedAt,
                TechnicalSummary = report.TechnicalSummary,
                FinancialSummary = report.FinancialSummary,
                Justification = report.Justification,
                RecommendedSupplier = report.RecommendedBid == null
                    || (report.RecommendedBid.Status != Statuses.BidStatus.Compliant && report.RecommendedBid.Status != Statuses.BidStatus.Accepted)
                    ? null : report.RecommendedBid.SupplierProfile.CompanyName,
                RecommendedBidPrice = report.RecommendedBid == null
                    || (report.RecommendedBid.Status != Statuses.BidStatus.Compliant && report.RecommendedBid.Status != Statuses.BidStatus.Accepted)
                    ? null : report.RecommendedBid.TotalPrice,
                RecommendedBidCurrency = report.RecommendedBid == null
                    || (report.RecommendedBid.Status != Statuses.BidStatus.Compliant && report.RecommendedBid.Status != Statuses.BidStatus.Accepted)
                    ? null : report.RecommendedBid.Currency
            })
            .FirstOrDefaultAsync();

        if (model == null)
        {
            // A report notification can be opened after the manager has already acted on it.
            // In that case it is no longer in the pending-review queue, so do not turn an old
            // notification into a 404. Open the saved final result when this manager can view it;
            // otherwise return to the manager dashboard.
            var reportState = await _context.EvaluationReports
                .AsNoTracking()
                .Where(report => report.TenderId == id)
                .Select(report => new { report.Status, TenderStatus = report.Tender.Status })
                .FirstOrDefaultAsync();

            if (reportState is null)
                return NotFound();

            var hasTopCommitteeResult = await _context.TenderApprovals
                .AsNoTracking()
                .AnyAsync(approval => approval.TenderId == id
                    && approval.Level == Statuses.ApprovalLevel.TopCommittee);
            var managerApprovedThisTender = await _context.TenderApprovals
                .AsNoTracking()
                .AnyAsync(approval => approval.TenderId == id
                    && approval.Level == Statuses.ApprovalLevel.Manager
                    && approval.DecidedByUserId == userId
                    && approval.Decision == Statuses.ApprovalDecision.Approved);

            if (hasTopCommitteeResult && managerApprovedThisTender)
                return RedirectToAction("TopResult", "Decisions", new { area = "Manager", id });

            return RedirectToAction("Index", "Dashboard", new { area = "Manager" });
        }

        model.RequestItems = await _context.PurchaseRequisitionItems
            .AsNoTracking()
            .Where(item => item.RequisitionId == model.RequisitionId)
            .OrderBy(item => item.Id)
            .Select(item => new ManagerReviewRequestItemViewModel
            {
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                Unit = item.Unit,
                Specifications = item.Specifications
            })
            .ToListAsync();

        model.CommitteeMembers = await _context.CommitteeMembers
            .AsNoTracking()
            .Where(member => member.TenderId == model.TenderId)
            .OrderBy(member => member.Id)
            .Select(member => new ManagerReviewCommitteeMemberViewModel
            {
                UserId = member.UserId,
                FullName = member.User.FullName,
                Role = member.MemberRole
            })
            .ToListAsync();

        model.Bids = await _context.Bids
            .AsNoTracking()
            .Where(bid => bid.TenderId == model.TenderId && bid.Status != Statuses.BidStatus.Draft)
            .OrderBy(bid => bid.TotalPrice)
            .Select(bid => new ManagerReviewBidViewModel
            {
                Id = bid.Id,
                SupplierName = bid.SupplierProfile.CompanyName,
                Status = bid.Status,
                TotalPrice = bid.TotalPrice,
                Currency = bid.Currency,
                DeliveryDays = bid.DeliveryDays,
                ValidityDays = bid.ValidityDays,
                PaymentTerms = bid.PaymentTerms,
                TechnicalSummary = bid.TechnicalSummary,
                SubmittedAt = bid.SubmittedAt
            })
                    .ToListAsync();

        var bidIds = model.Bids.Select(bid => bid.Id).ToList();
        if (bidIds.Count > 0)
        {
            var evaluationCriteria = await _context.EvaluationCriteria
                .AsNoTracking()
                .Where(criterion => criterion.TenderId == model.TenderId)
                .ToListAsync();
            var technicalCriteria = evaluationCriteria
                .Where(criterion => criterion.Type == Statuses.CriterionType.Technical)
                .ToList();
            var financialCriteria = evaluationCriteria
                .Where(criterion => criterion.Type == Statuses.CriterionType.Financial)
                .ToList();

            // TechnicalEvaluations is the legacy table for answers to both criterion types.
            // Keep every row for weighted completeness calculations, but show only technical
            // questions in the technical section.
            var savedEvaluationRows = await _context.TechnicalEvaluations
                .AsNoTracking()
                .Where(score => bidIds.Contains(score.BidId))
                .Include(score => score.Criterion)
                .OrderByDescending(score => score.EvaluatedAt)
                .ToListAsync();
            var technicalScoreRows = savedEvaluationRows
                .Where(score => score.Criterion.Type == Statuses.CriterionType.Technical)
                .Select(score => new
                {
                    score.BidId,
                    score.EvaluatorUserId,
                    Item = new ManagerReviewTechnicalScoreViewModel
                    {
                        CriterionName = score.Criterion.Name,
                        WeightPercent = score.Criterion.WeightPercent,
                        MaxScore = score.Criterion.MaxScore,
                        Score = score.Score,
                        Notes = score.Notes
                    }
                })
                .ToList();

            var financialScoreRows = await _context.FinancialEvaluations
                .AsNoTracking()
                .Where(score => bidIds.Contains(score.BidId))
                .OrderByDescending(score => score.EvaluatedAt)
                .Select(score => new
                {
                    score.BidId,
                    score.EvaluatorUserId,
                    Item = new ManagerReviewFinancialScoreViewModel
                    {
                        Score = score.Score,
                        TotalCost = score.TotalCost,
                        TaxesAndFees = score.TaxesAndFees,
                        LogisticsCost = score.LogisticsCost,
                        Notes = score.Notes,
                        ReadyAt = score.ReadyAt
                    }
                })
                .ToListAsync();

            var bidItemRows = await _context.BidItems
                .AsNoTracking()
                .Where(item => bidIds.Contains(item.BidId))
                .Select(item => new
                {
                    item.BidId,
                    Item = new ManagerReviewBidItemViewModel
                    {
                        ItemName = item.RequisitionItem.ItemName,
                        RequestedQuantity = item.RequisitionItem.Quantity,
                        Unit = item.RequisitionItem.Unit,
                        OfferedQuantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        TaxAmount = item.TaxAmount,
                        ShippingCost = item.ShippingCost,
                        InstallationCost = item.InstallationCost,
                        OfferedSpecifications = item.OfferedSpecifications
                    }
                })
                .ToListAsync();

            var complianceNoteRows = await _context.ComplianceChecks
                .AsNoTracking()
                .Where(check => bidIds.Contains(check.BidId))
                .OrderByDescending(check => check.CheckedAt)
                .Select(check => new
                {
                    check.BidId,
                    Note = string.IsNullOrWhiteSpace(check.Reason) ? check.Result : $"{check.Result}: {check.Reason}"
                })
                .ToListAsync();

            var bidDocumentRows = await _context.BidDocuments
                .AsNoTracking()
                .Where(document => bidIds.Contains(document.BidId))
                .OrderBy(document => document.FileName)
                .Select(document => new
                {
                    document.BidId,
                    Item = new ManagerReviewBidDocumentViewModel
                    {
                        Id = document.Id,
                        DocType = document.DocType,
                        FileName = document.FileName,
                        ContentType = document.ContentType,
                        SizeBytes = document.SizeBytes
                    }
                })
                .ToListAsync();

            foreach (var bid in model.Bids)
            {
                bid.Items = bidItemRows
                    .Where(item => item.BidId == bid.Id)
                    .Select(item => item.Item)
                    .ToList();
                bid.ComplianceNotes = complianceNoteRows
                    .Where(check => check.BidId == bid.Id)
                    .Select(check => check.Note)
                    .ToList();
                bid.Documents = bidDocumentRows
                    .Where(document => document.BidId == bid.Id)
                    .Select(document => document.Item)
                    .ToList();
                bid.Evaluations = model.CommitteeMembers.Select(member =>
                {
                    var memberEvaluationRows = savedEvaluationRows
                        .Where(score => score.BidId == bid.Id)
                        .ToList();
                    var memberTechnicalScores = technicalScoreRows
                        .Where(score => score.BidId == bid.Id && score.EvaluatorUserId == member.UserId)
                        .Select(score => score.Item)
                        .ToList();
                    var memberFinancialScore = financialScoreRows
                        .Where(score => score.BidId == bid.Id && score.EvaluatorUserId == member.UserId)
                        .Select(score => score.Item)
                        .FirstOrDefault();
                    var technicalPoints = evaluationCriteria.Count == 0
                        ? (decimal?)null
                        : EvaluationScoring.Points(technicalCriteria, memberEvaluationRows, member.UserId);

                    return new ManagerReviewEvaluatorResultViewModel
                    {
                        EvaluatorName = member.FullName,
                        CommitteeRole = member.Role,
                        TechnicalScores = memberTechnicalScores,
                        TechnicalPoints = technicalPoints,
                        FinancialScore = memberFinancialScore
                    };
                }).ToList();

                if (model.CommitteeMembers.Count == 4
                    && bid.Evaluations.Count == 4
                    && financialCriteria.Count > 0
                    && bid.Evaluations.All(result => result.TechnicalPoints.HasValue
                        && result.FinancialScore?.ReadyAt != null
                        && result.FinancialScore.Score.HasValue))
                {
                    bid.CommitteeTechnicalAverage = bid.Evaluations.Average(result => result.TechnicalPoints!.Value);
                    bid.CommitteeFinancialAverage = bid.Evaluations.Average(result => result.FinancialScore!.Score!.Value);
                    bid.CommitteeTotalAverage = bid.CommitteeTechnicalAverage + bid.CommitteeFinancialAverage;
                }
            }
        }

        model.AiAnalyses = await _context.AiBidAnalyses
            .AsNoTracking()
            .Where(analysis => analysis.TenderId == model.TenderId)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .Select(analysis => new ManagerAiAnalysisViewModel
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
            .ToListAsync();

        model.Clarifications = await _context.Clarifications
            .AsNoTracking()
            .Where(clarification => clarification.TenderId == model.TenderId
                || (clarification.BidId != null && clarification.Bid != null && clarification.Bid.TenderId == model.TenderId))
            .OrderByDescending(clarification => clarification.CreatedAt)
            .Select(clarification => new ManagerClarificationThreadViewModel
            {
                Id = clarification.Id,
                Subject = clarification.Subject,
                Description = clarification.Description,
                Status = clarification.Status,
                CreatedAt = clarification.CreatedAt
            })
            .ToListAsync();

        var clarificationIds = model.Clarifications.Select(clarification => clarification.Id).ToList();
        if (clarificationIds.Count > 0)
        {
            var messages = await _context.ClarificationMessages
                .AsNoTracking()
                .Where(message => clarificationIds.Contains(message.ClarificationId))
                .OrderBy(message => message.SentAt)
                .Select(message => new
                {
                    message.ClarificationId,
                    Item = new ManagerClarificationMessageViewModel
                    {
                        SenderName = message.SenderUser.FullName,
                        Body = message.Body,
                        SentAt = message.SentAt
                    }
                })
                .ToListAsync();

            foreach (var thread in model.Clarifications)
                thread.Messages = messages
                    .Where(message => message.ClarificationId == thread.Id)
                    .Select(message => message.Item)
                    .ToList();
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(int tenderId, int documentId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var document = await _context.BidDocuments
            .AsNoTracking()
            .Where(item => item.Id == documentId
                && item.Bid.TenderId == tenderId
                && item.Bid.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                && item.Bid.Tender.EvaluationReports.Any(report => report.Status == Statuses.ReportStatus.RecommendationPending))
            .Select(item => new { item.FilePath, item.FileName, item.ContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        try
        {
            await using var stream = await _fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
            if (stream is null) return NotFound();

            var viewer = await _context.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new { user.FullName, user.Email })
                .SingleOrDefaultAsync(cancellationToken);
            if (viewer is null) return Challenge();

            var downloadedAt = DateTime.UtcNow;
            var viewerName = string.IsNullOrWhiteSpace(viewer.FullName) ? viewer.Email ?? "Signed-in user" : viewer.FullName;
            var webRoot = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
            var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(
                _context, userId, "BidDocument", documentId, Statuses.DocumentAction.Download, downloadedAt, cancellationToken);
            var watermarkedContent = await DocumentWatermarkService.CreateWatermarkedCopyAsync(
                stream, document.FileName, viewerName, downloadedAt, webRoot, cancellationToken, history);

            if (string.Equals(document.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                Response.Headers.CacheControl = "no-store";
            return File(watermarkedContent,
                string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType,
                Path.GetFileName(document.FileName));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
