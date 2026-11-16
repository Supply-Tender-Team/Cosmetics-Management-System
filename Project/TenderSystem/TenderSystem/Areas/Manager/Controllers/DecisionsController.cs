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

        var selectedBid = report.RecommendedBidId.HasValue
            ? await _db.Bids
                .Where(bid => bid.Id == report.RecommendedBidId.Value
                    && bid.TenderId == id
                    && (bid.Status == Statuses.BidStatus.Compliant || bid.Status == Statuses.BidStatus.Accepted))
                .Select(bid => new
                {
                    bid.Id,
                    bid.SupplierProfileId,
                    SupplierName = bid.SupplierProfile.CompanyName,
                    bid.TotalPrice,
                    bid.Currency
                })
                .FirstOrDefaultAsync()
            : null;

        if (input.Decision == Statuses.ApprovalDecision.Approved && selectedBid is null)
            ModelState.AddModelError(nameof(input.Decision), "The committee's selected eligible offer is missing from this report. Return the report for correction.");

        if (!ModelState.IsValid)
        {
            var page = await BuildPageModelAsync(id, userId, input);
            if (page == null)
                return NotFound();
            return View("Index", page);
        }

        var approvalComment = input.Decision == Statuses.ApprovalDecision.Approved && report.Tender.RequiresTopCommittee
            ? await BuildTopCommitteeSummaryAsync(id, selectedBid!.Id, input.Comment.Trim())
            : input.Comment.Trim();
        if (approvalComment.Length > 1000)
        {
            ModelState.AddModelError(nameof(input.Comment), "The generated summary is longer than the decision record can hold. Return the report for revision so the offer package can be shortened.");
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
            SelectedBidId = input.Decision == Statuses.ApprovalDecision.Approved ? selectedBid!.Id : null,
            Comment = approvalComment,
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
                report.Tender.AwardedSupplierId = selectedBid!.SupplierProfileId;
            }
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
                $"{report.Tender.TenderNumber}: manager summary and {selectedBid!.SupplierName} recommendation are ready for review.",
                $"/Committee/Review?id={report.TenderId}");
        }
        else if (input.Decision == Statuses.ApprovalDecision.Returned)
        {
            await _notifications.NotifyAsync(
                report.Tender.CreatedByUserId,
                "ManagerDecisionRecorded",
                "Report returned for correction",
                $"{report.Tender.TenderNumber}: the Manager returned the evaluation report for correction. " +
                $"Manager note: {approval.Comment} Open the Sourcing evaluation page to review it and continue.",
                $"/Sourcing/Evaluation?tenderId={report.TenderId}");
        }

        TempData["ManagerDecisionMessage"] = input.Decision switch
        {
            Statuses.ApprovalDecision.Approved when report.Tender.RequiresTopCommittee => "Report approved and forwarded to the Top Committee.",
            Statuses.ApprovalDecision.Approved => "Report approved and tender awarded to the recommended supplier.",
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
                RecommendedSupplier = report.RecommendedBid == null
                    || (report.RecommendedBid.Status != Statuses.BidStatus.Compliant && report.RecommendedBid.Status != Statuses.BidStatus.Accepted)
                    ? null : report.RecommendedBid.SupplierProfile.CompanyName,
                RecommendedBidPrice = report.RecommendedBid == null
                    || (report.RecommendedBid.Status != Statuses.BidStatus.Compliant && report.RecommendedBid.Status != Statuses.BidStatus.Accepted)
                    ? null : report.RecommendedBid.TotalPrice,
                RecommendedBidCurrency = report.RecommendedBid == null
                    || (report.RecommendedBid.Status != Statuses.BidStatus.Compliant && report.RecommendedBid.Status != Statuses.BidStatus.Accepted)
                    ? null : report.RecommendedBid.Currency,
                Input = input ?? new ManagerDecisionInputViewModel()
            })
            .FirstOrDefaultAsync();
    }

    private async Task<string> BuildTopCommitteeSummaryAsync(int tenderId, int selectedBidId, string managerNote)
    {
        var tender = await _db.Tenders
            .AsNoTracking()
            .Where(item => item.Id == tenderId)
            .Select(item => new
            {
                item.TenderNumber,
                item.Title,
                Department = item.Requisition.Department.Name,
                item.Requisition.TargetDate,
                item.EstimatedValue
            })
            .SingleAsync();

        var offers = await _db.Bids
            .AsNoTracking()
            .Where(bid => bid.TenderId == tenderId && bid.Status != Statuses.BidStatus.Draft)
            .OrderBy(bid => bid.TotalPrice)
            .Select(bid => new
            {
                bid.Id,
                Supplier = bid.SupplierProfile.CompanyName,
                bid.Status,
                bid.TotalPrice,
                bid.Currency,
                bid.DeliveryDays
            })
            .ToListAsync();

        var bidIds = offers.Select(offer => offer.Id).ToList();
        var technicalRows = await _db.TechnicalEvaluations
            .AsNoTracking()
            .Where(score => bidIds.Contains(score.BidId))
            .Select(score => new
            {
                score.BidId,
                score.EvaluatorUserId,
                score.Score,
                MaxScore = score.Criterion.MaxScore,
                score.Criterion.WeightPercent
            })
            .ToListAsync();
        var financialRows = await _db.FinancialEvaluations
            .AsNoTracking()
            .Where(score => bidIds.Contains(score.BidId) && score.ReadyAt != null)
            .Select(score => new { score.BidId, score.Score })
            .ToListAsync();

        var summary = new System.Text.StringBuilder("Top Committee summary");
        summary.AppendLine();
        summary.Append("Tender: ").Append(tender.TenderNumber).Append(" | ").Append(tender.Title);
        summary.AppendLine();
        summary.Append("Department: ").Append(tender.Department);
        summary.Append(" | Needed by: ").Append(tender.TargetDate.ToString("dd MMM yyyy"));
        summary.Append(" | Estimated value: ").Append(tender.EstimatedValue.ToString("N2"));
        summary.AppendLine().Append("Offers:");
        foreach (var offer in offers)
        {
            var memberTechnicalScores = technicalRows
                .Where(score => score.BidId == offer.Id)
                .GroupBy(score => score.EvaluatorUserId)
                .Select(memberScores =>
                {
                    var completeScores = memberScores
                        .Where(score => score.Score.HasValue && score.MaxScore > 0)
                        .ToList();
                    if (completeScores.Count != memberScores.Count()) return (decimal?)null;
                    return completeScores.Sum(score => score.Score!.Value / score.MaxScore * score.WeightPercent);
                })
                .Where(score => score.HasValue)
                .Select(score => score!.Value)
                .ToList();
            var readyFinancialScores = financialRows
                .Where(score => score.BidId == offer.Id && score.Score.HasValue)
                .Select(score => score.Score!.Value)
                .ToList();
            var technicalAverage = memberTechnicalScores.Count > 0 ? memberTechnicalScores.Average() : (decimal?)null;
            var financialAverage = readyFinancialScores.Count > 0 ? readyFinancialScores.Average() : (decimal?)null;
            var totalAverage = technicalAverage.HasValue && financialAverage.HasValue
                ? technicalAverage.Value + financialAverage.Value
                : (decimal?)null;

            summary.AppendLine();
            summary.Append("• ").Append(offer.Supplier).Append(" | ").Append(offer.Status);
            summary.Append(" | ").Append(offer.TotalPrice.ToString("N2")).Append(' ').Append(offer.Currency);
            summary.Append(" | ").Append(offer.DeliveryDays).Append(" days");
            summary.Append(" | Tech ").Append(technicalAverage?.ToString("N1") ?? "Pending").Append("/80");
            summary.Append(" | Fin ").Append(financialAverage?.ToString("N1") ?? "Pending").Append("/20");
            summary.Append(" | Total ").Append(totalAverage?.ToString("N1") ?? "Pending").Append("/100");
            if (offer.Id == selectedBidId) summary.Append(" | COMMITTEE SELECTED");
        }

        summary.AppendLine().AppendLine().Append("Manager note: ").Append(managerNote);
        return summary.ToString();
    }
}
