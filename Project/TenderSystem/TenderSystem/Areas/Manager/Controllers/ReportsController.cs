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
public class ReportsController : Controller
{
    // OWNER: Dana

    private readonly ApplicationDbContext _db;

    public ReportsController(ApplicationDbContext db)
    {
        _db = db;
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

        var model = await _db.EvaluationReports
            .AsNoTracking()
            .Where(report => report.TenderId == id
                && report.Status == Statuses.ReportStatus.RecommendationPending
                && report.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview
                && report.Tender.CommitteeMembers.Any(member => member.UserId == userId
                    && member.MemberRole == Statuses.CommitteeRole.SourcingManager))
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
            return NotFound();

        model.RequestItems = await _db.PurchaseRequisitionItems
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

        model.CommitteeMembers = await _db.CommitteeMembers
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

        model.Bids = await _db.Bids
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
            var technicalScores = await _db.TechnicalEvaluations
                .AsNoTracking()
                .Where(score => bidIds.Contains(score.BidId))
                .OrderByDescending(score => score.EvaluatedAt)
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
                .ToListAsync();

            var financialScores = await _db.FinancialEvaluations
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

            var bidItems = await _db.BidItems
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

            var complianceNotes = await _db.ComplianceChecks
                .AsNoTracking()
                .Where(check => bidIds.Contains(check.BidId))
                .OrderByDescending(check => check.CheckedAt)
                .Select(check => new
                {
                    check.BidId,
                    Note = string.IsNullOrWhiteSpace(check.Reason) ? check.Result : $"{check.Result}: {check.Reason}"
                })
                .ToListAsync();

            var bidDocuments = await _db.BidDocuments
                .AsNoTracking()
                .Where(document => bidIds.Contains(document.BidId))
                .OrderBy(document => document.FileName)
                .Select(document => new { document.BidId, Name = $"{document.DocType}: {document.FileName}" })
                .ToListAsync();

            foreach (var bid in model.Bids)
            {
                bid.Items = bidItems
                    .Where(item => item.BidId == bid.Id)
                    .Select(item => item.Item)
                    .ToList();
                bid.ComplianceNotes = complianceNotes
                    .Where(check => check.BidId == bid.Id)
                    .Select(check => check.Note)
                    .ToList();
                bid.Documents = bidDocuments
                    .Where(document => document.BidId == bid.Id)
                    .Select(document => document.Name)
                    .ToList();
                bid.Evaluations = model.CommitteeMembers.Select(member =>
                {
                    var memberTechnicalScores = technicalScores
                        .Where(score => score.BidId == bid.Id && score.EvaluatorUserId == member.UserId)
                        .Select(score => score.Item)
                        .ToList();
                    var memberFinancialScore = financialScores
                        .Where(score => score.BidId == bid.Id && score.EvaluatorUserId == member.UserId)
                        .Select(score => score.Item)
                        .FirstOrDefault();
                    var hasCompleteTechnicalScores = memberTechnicalScores.Count > 0
                        && memberTechnicalScores.All(score => score.Score.HasValue && score.MaxScore > 0);

                    return new ManagerReviewEvaluatorResultViewModel
                    {
                        EvaluatorName = member.FullName,
                        CommitteeRole = member.Role,
                        TechnicalScores = memberTechnicalScores,
                        TechnicalPoints = hasCompleteTechnicalScores
                            ? memberTechnicalScores.Sum(score => score.Score!.Value / score.MaxScore * score.WeightPercent)
                            : null,
                        FinancialScore = memberFinancialScore
                    };
                }).ToList();

                if (bid.Evaluations.Count == 4 && bid.Evaluations.All(result => result.TechnicalPoints.HasValue && result.FinancialScore?.Score.HasValue == true))
                {
                    bid.CommitteeTechnicalAverage = bid.Evaluations.Average(result => result.TechnicalPoints!.Value);
                    bid.CommitteeFinancialAverage = bid.Evaluations.Average(result => result.FinancialScore!.Score!.Value);
                    bid.CommitteeTotalAverage = bid.CommitteeTechnicalAverage + bid.CommitteeFinancialAverage;
                }
            }
        }

        model.AiAnalyses = await _db.AiBidAnalyses
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

        model.Clarifications = await _db.Clarifications
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
            var messages = await _db.ClarificationMessages
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
}
