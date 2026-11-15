using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Notifications;
using TenderSystem.Areas.Evaluation.Controllers;
using TenderSystem.Areas.Evaluation.Models;

namespace TenderSystem.Areas.Evaluation.Services;

public class EvaluationReportService
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notify;
    private readonly AuditService _audit;

    public EvaluationReportService(ApplicationDbContext db, NotificationService notify, AuditService audit)
    {
        _db = db;
        _notify = notify;
        _audit = audit;
    }

    // Called inside Ready's transaction, after locking the tender and saving the form.
    public async Task<bool> TryCreateAsync(int tenderId, string userId, string reportLink)
    {
        if (await _db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId)) return false;
        var tender = await _db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.Requisition).ThenInclude(r => r.Department)
            .Include(t => t.EvaluationCriteria).Include(t => t.CommitteeMembers).ThenInclude(m => m.User)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.ComplianceChecks)
            .Include(t => t.Bids).ThenInclude(b => b.BidItems).ThenInclude(i => i.RequisitionItem)
            .SingleAsync(t => t.Id == tenderId);
        var criteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).OrderBy(c => c.Id).ToList();
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        var members = tender.CommitteeMembers.OrderBy(m => m.Id).ToList();
        var offers = tender.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted).ToList();
        if (criteria.Sum(c => c.WeightPercent) is <= 0 or > 80
            || tender.EvaluationCriteria.Count(c => c.Type == Statuses.CriterionType.Financial) != 1
            || tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent) != 20
            || tender.EvaluationCriteria.Any(c => !c.IsLocked || c.MaxScore <= 0 || c.WeightPercent <= 0)
            || members.Count != 4 || members.Select(m => m.UserId).Distinct().Count() != 4
            || seats.Any(seat => members.Count(m => m.MemberRole == seat) != 1)
            || offers.Count == 0 || offers.Any(b => b.SubmittedAt == null || b.TotalPrice <= 0 || string.IsNullOrWhiteSpace(b.Currency))
            || offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1
            || tender.Bids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview)) return false;
        var prices = offers.Select(b => b.TotalPrice).Distinct().OrderBy(p => p).ToList();
        var technicalWeight = criteria.Sum(c => c.WeightPercent);
        var technical = new StringBuilder($"Confirmed committee technical results ({technicalWeight:0.##} points).\n");
        var financial = new StringBuilder("Distinct-price financial ranking (20 points).\n");
        technical.AppendLine($"{tender.TenderNumber} — {tender.Title}; request {tender.Requisition.RequestNumber}; department {tender.Requisition.Department.Name}.");
        technical.AppendLine($"Generated {DateTime.UtcNow:O}. All four members confirmed every eligible offer.");
        foreach (var criterion in criteria) technical.AppendLine($"Question: {criterion.Name}; maximum {criterion.MaxScore}; weight {criterion.WeightPercent:0.##}%.");
        foreach (var excluded in tender.Bids.Where(b => b.Status != Statuses.BidStatus.Draft && !offers.Any(o => o.Id == b.Id)))
        {
            var check = excluded.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id).FirstOrDefault();
            technical.AppendLine($"Excluded offer #{excluded.Id} — {excluded.SupplierProfile.CompanyName}: {excluded.Status}. Review: {check?.Result ?? "No recorded decision"}. Reason: {check?.Reason ?? "No recorded reason"}.");
        }
        var totals = new List<(int BidId, string Supplier, decimal Total)>();
        foreach (var bid in offers.OrderBy(b => b.TotalPrice))
        {
            var memberPoints = new List<decimal>();
            technical.AppendLine($"Offer #{bid.Id} — {bid.SupplierProfile.CompanyName}");
            var financialPoints = AssessmentsController.CalculateFinancialPoints(prices, bid.TotalPrice, 20)!.Value;
            foreach (var member in members)
            {
                var confirmation = bid.FinancialEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId);
                if (confirmation?.ReadyAt == null || confirmation.TotalCost != bid.TotalPrice
                    || confirmation.Score != decimal.Round(financialPoints, 2)) return false;
                var scores = new List<EvaluationScoreViewModel>();
                foreach (var criterion in criteria)
                {
                    var saved = bid.TechnicalEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.Id);
                    if (saved?.Score == null || saved.Score != decimal.Round(saved.Score.Value, 2)) return false;
                    scores.Add(new EvaluationScoreViewModel { Score = saved.Score, MaxScore = criterion.MaxScore, WeightPercent = criterion.WeightPercent });
                    technical.AppendLine($"{member.User.FullName} — {criterion.Name}: {saved.Score:0.##}/{criterion.MaxScore}; weighted {saved.Score.Value / criterion.MaxScore * criterion.WeightPercent:0.##}.");
                    if (!string.IsNullOrWhiteSpace(saved.Notes)) technical.AppendLine($"{member.User.FullName} — {criterion.Name}: {saved.Notes}");
                }
                var points = AssessmentsController.CalculateTechnicalPoints(scores);
                if (!points.HasValue) return false;
                memberPoints.Add(points.Value);
                technical.AppendLine($"{member.MemberRole} — {member.User.FullName}: {points:0.##}/{technicalWeight:0.##}; Ready {confirmation.ReadyAt:O}");
                if (!string.IsNullOrWhiteSpace(confirmation.Notes)) financial.AppendLine($"{member.User.FullName}: {confirmation.Notes}");
            }
            var average = memberPoints.Average();
            technical.AppendLine($"Committee average: {average:0.##}/{technicalWeight:0.##}");
            financial.AppendLine($"Offer #{bid.Id} — {bid.SupplierProfile.CompanyName}: {bid.TotalPrice:0.00} {bid.Currency}; financial {financialPoints:0.##}/20; total {average + financialPoints:0.##}/{technicalWeight + 20:0.##}.");
            financial.AppendLine($"Delivery {bid.DeliveryDays} days; validity {bid.ValidityDays} days; payment: {bid.PaymentTerms}.");
            foreach (var item in bid.BidItems)
                financial.AppendLine($"{item.RequisitionItem.ItemName}: quantity {item.Quantity:0.##}; unit {item.UnitPrice:0.00}; tax {item.TaxAmount:0.00}; shipping {item.ShippingCost:0.00}; installation {item.InstallationCost ?? 0:0.00}. These costs are already included in the supplier total.");
            totals.Add((bid.Id, bid.SupplierProfile.CompanyName, average + financialPoints));
        }
        financial.AppendLine("Results ranking, not an award decision:");
        foreach (var row in totals.OrderByDescending(r => r.Total))
            financial.AppendLine($"Rank {1 + totals.Count(other => other.Total > row.Total)}: offer #{row.BidId} — {row.Supplier}: {row.Total:0.##}/{technicalWeight + 20:0.##}.");
        var report = new EvaluationReport
        {
            TenderId = tenderId, Status = Statuses.ReportStatus.RecommendationPending,
            PreparedByUserId = userId, CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow,
            TechnicalSummary = technical.ToString(), FinancialSummary = financial.ToString(),
            RecommendedBidId = null,
            Justification = "Automatically generated results after all committee forms were confirmed Ready. No human supplier recommendation has been recorded."
        };
        _db.EvaluationReports.Add(report);
        await _db.SaveChangesAsync();
        await _notify.NotifyAsync(tender.CreatedByUserId, "EvaluationReportReady", "Full evaluation report ready",
            $"All four members confirmed every eligible offer for {tender.TenderNumber}. Open View details to read the full report, including supplier comparison, member scores and comments.", reportLink);
        await _audit.LogAsync("SubmittedToOfficer", "EvaluationReport", report.Id.ToString(), "Automatically generated after all required Ready confirmations.");
        return true;
    }
}
