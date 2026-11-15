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
                ReportId = report.Id,
                RecommendedBidId = report.RecommendedBidId,
                TenderNumber = report.Tender.TenderNumber,
                TenderTitle = report.Tender.Title,
                TenderStatus = report.Tender.Status,
                RequestNumber = report.Tender.Requisition.RequestNumber,
                RequestTitle = report.Tender.Requisition.Title,
                DepartmentName = report.Tender.Requisition.Department.Name,
                EstimatedValue = report.Tender.EstimatedValue,
                RequiresTopCommittee = report.Tender.RequiresTopCommittee,
                ReportStatus = report.Status,
                SubmittedAt = report.SubmittedAt ?? report.CreatedAt,
                TechnicalSummary = report.TechnicalSummary,
                FinancialSummary = report.FinancialSummary,
                Justification = report.Justification,
                RecommendedSupplier = report.RecommendedBid == null ? null : report.RecommendedBid.SupplierProfile.CompanyName,
                RecommendedBidPrice = report.RecommendedBid == null ? null : report.RecommendedBid.TotalPrice,
                RecommendedBidCurrency = report.RecommendedBid == null ? null : report.RecommendedBid.Currency
            })
            .FirstOrDefaultAsync();

        if (model == null)
            return NotFound();

        model.Bids = await _db.Bids
            .AsNoTracking()
            .Where(bid => bid.TenderId == model.TenderId)
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
                    Item = new ManagerReviewFinancialScoreViewModel
                    {
                        Score = score.Score,
                        TotalCost = score.TotalCost,
                        TaxesAndFees = score.TaxesAndFees,
                        LogisticsCost = score.LogisticsCost,
                        Notes = score.Notes
                    }
                })
                .ToListAsync();

            foreach (var bid in model.Bids)
            {
                bid.TechnicalScores = technicalScores
                    .Where(score => score.BidId == bid.Id)
                    .Select(score => score.Item)
                    .ToList();
                bid.FinancialScores = financialScores
                    .Where(score => score.BidId == bid.Id)
                    .Select(score => score.Item)
                    .ToList();
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
