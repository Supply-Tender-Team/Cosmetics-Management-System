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

    // Called inside the final vote transaction, after taking the shared tender lock.
    public async Task<bool> TryCreateAsync(int tenderId, string userId, string reportLink, string sharedLink)
    {
        if (await _db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId)) return false;
        var state = await new CommitteeRecommendationService(_db).ReadAsync(tenderId, userId, sharedLink);
        if (state == null || !state.AllReady || state.ConfigurationMessage != null) return false;
        var votes = await _db.EvaluationCommitteeSubmissions.AsNoTracking().Where(s => s.TenderId == tenderId
            && _db.CommitteeMembers.Any(m => m.TenderId == tenderId && m.UserId == s.EvaluatorUserId)).ToListAsync();
        var winner = CommitteeRecommendationService.ResolveWinner(votes, state.Offers);
        if (!winner.HasValue) return false;
        var tender = await _db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.Requisition).ThenInclude(r => r.Department)
            .Include(t => t.EvaluationCriteria).Include(t => t.CommitteeMembers).ThenInclude(m => m.User)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.ComplianceChecks)
            .Include(t => t.Bids).ThenInclude(b => b.BidItems).ThenInclude(i => i.RequisitionItem)
            .SingleAsync(t => t.Id == tenderId);
        if (tender.Status != Statuses.TenderStatus.Published && tender.Status != Statuses.TenderStatus.Open
            && tender.Status != Statuses.TenderStatus.Closed && tender.Status != Statuses.TenderStatus.UnderEvaluation) return false;
        var criteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).OrderBy(c => c.Id).ToList();
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        var members = tender.CommitteeMembers.OrderBy(m => m.Id).ToList();
        var offers = tender.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted).ToList();
        if (!EvaluationScoring.ValidRubric(tender.EvaluationCriteria)
            || members.Count != 4 || members.Select(m => m.UserId).Distinct().Count() != 4
            || seats.Any(seat => members.Count(m => m.MemberRole == seat) != 1)
            || offers.Count == 0 || offers.Any(b => b.SubmittedAt == null || b.TotalPrice <= 0 || string.IsNullOrWhiteSpace(b.Currency))
            || offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1
            || tender.Bids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview)) return false;
        var technicalWeight = criteria.Sum(c => c.WeightPercent);
        var technical = new StringBuilder($"Confirmed committee technical results ({technicalWeight:0.##} points).\n");
        var financialWeight = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent);
        var financial = new StringBuilder($"Confirmed committee financial results ({financialWeight:0.##} points).\n");
        technical.AppendLine($"{tender.TenderNumber} — {tender.Title}; request {tender.Requisition.RequestNumber}; department {tender.Requisition.Department.Name}.");
        technical.AppendLine($"Generated {DateTime.UtcNow:O}. All four members confirmed every eligible offer.");
        foreach (var criterion in tender.EvaluationCriteria) (criterion.Type == Statuses.CriterionType.Technical ? technical : financial).AppendLine($"Question: {criterion.Name}; maximum {criterion.MaxScore}; weight {criterion.WeightPercent:0.##}%.");
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
            var financialMemberPoints = new List<decimal>();
            foreach (var member in members)
            {
                var financialPoints = EvaluationScoring.Points(tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial), bid.TechnicalEvaluations, member.UserId);
                if (!financialPoints.HasValue) return false;
                financialMemberPoints.Add(financialPoints.Value);
                var confirmation = bid.FinancialEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId);
                if (confirmation?.ReadyAt == null || confirmation.TotalCost != bid.TotalPrice
                    || confirmation.Score != decimal.Round(financialPoints.Value, 2)) return false;
                var scores = new List<EvaluationScoreViewModel>();
                foreach (var criterion in tender.EvaluationCriteria)
                {
                    var saved = bid.TechnicalEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.Id);
                    if (saved?.Score == null || saved.Score != decimal.Round(saved.Score.Value, 2)) return false;
                    if (criterion.Type == Statuses.CriterionType.Technical) scores.Add(new EvaluationScoreViewModel { Score = saved.Score, MaxScore = criterion.MaxScore, WeightPercent = criterion.WeightPercent });
                    (criterion.Type == Statuses.CriterionType.Technical ? technical : financial).AppendLine($"{member.User.FullName} — {criterion.Name}: {saved.Score:0.##}/{criterion.MaxScore}; weighted {saved.Score.Value / criterion.MaxScore * criterion.WeightPercent:0.##}.");
                    if (!string.IsNullOrWhiteSpace(saved.Notes)) (criterion.Type == Statuses.CriterionType.Technical ? technical : financial).AppendLine($"{member.User.FullName} — {criterion.Name}: {saved.Notes}");
                }
                var points = AssessmentsController.CalculateTechnicalPoints(scores);
                if (!points.HasValue) return false;
                memberPoints.Add(points.Value);
                technical.AppendLine($"{member.MemberRole} — {member.User.FullName}: {points:0.##}/{technicalWeight:0.##}; Ready {confirmation.ReadyAt:O}");
                if (!string.IsNullOrWhiteSpace(confirmation.Notes)) financial.AppendLine($"{member.User.FullName}: {confirmation.Notes}");
            }
            var average = memberPoints.Average();
            var financialPointsAverage = financialMemberPoints.Average();
            technical.AppendLine($"Committee average: {average:0.##}/{technicalWeight:0.##}");
            financial.AppendLine($"Offer #{bid.Id} — {bid.SupplierProfile.CompanyName}: {bid.TotalPrice:0.00} {bid.Currency}; financial {financialPointsAverage:0.##}/{financialWeight:0.##}; total {average + financialPointsAverage:0.##}/{technicalWeight + financialWeight:0.##}.");
            financial.AppendLine($"Delivery {bid.DeliveryDays} days; validity {bid.ValidityDays} days; payment: {bid.PaymentTerms}.");
            foreach (var item in bid.BidItems)
                financial.AppendLine($"{item.RequisitionItem.ItemName}: quantity {item.Quantity:0.##}; unit {item.UnitPrice:0.00}; tax {item.TaxAmount:0.00}; shipping {item.ShippingCost:0.00}; installation {item.InstallationCost ?? 0:0.00}. These costs are already included in the supplier total.");
            totals.Add((bid.Id, bid.SupplierProfile.CompanyName, average + financialPointsAverage));
        }
        financial.AppendLine("Results ranking, not an award decision:");
        foreach (var row in totals.OrderByDescending(r => r.Total))
            financial.AppendLine($"Rank {1 + totals.Count(other => other.Total > row.Total)}: offer #{row.BidId} — {row.Supplier}: {row.Total:0.##}/{technicalWeight + financialWeight:0.##}.");
        var report = new EvaluationReport
        {
            TenderId = tenderId, Status = Statuses.ReportStatus.RecommendationPending,
            PreparedByUserId = userId, CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow,
            TechnicalSummary = technical.ToString(), FinancialSummary = financial.ToString(),
            RecommendedBidId = winner.Value,
            Justification = "Committee recommendation: " + offers.Single(b => b.Id == winner.Value).SupplierProfile.CompanyName
                + ". Voting round " + votes[0].VotingRound + ". "
                + string.Join("; ", votes.GroupBy(v => v.RecommendedBidId).OrderByDescending(g => g.Count())
                    .Select(g => "Offer #" + g.Key + ": " + g.Count() + " vote(s)"))
                + ". The unique vote leader wins; tied vote leaders are compared by the final committee total rounded to two decimals."
        };
        await _db.Tenders.Where(t => t.Id == tenderId)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.Status, Statuses.TenderStatus.UnderEvaluation));
        _db.EvaluationReports.Add(report);
        await _db.SaveChangesAsync();
        await _notify.NotifyAsync(tender.CreatedByUserId, "EvaluationReportReady", "Full evaluation report ready",
            $"All four members confirmed their recommendations for {tender.TenderNumber}. The full report and recommended offer are available in Evaluation tracking for your review.", reportLink);
        await _audit.LogAsync("SubmittedToOfficer", "EvaluationReport", report.Id.ToString(), $"Automatically generated after four recommendation votes; recommendedBid={winner.Value}.");
        return true;
    }
}
