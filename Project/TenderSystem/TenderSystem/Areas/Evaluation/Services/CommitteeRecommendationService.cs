using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Controllers;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Services;

public class CommitteeRecommendationService
{
    private readonly ApplicationDbContext _db;
    public CommitteeRecommendationService(ApplicationDbContext db) { _db = db; }

    public async Task<CommitteeRecommendationViewModel?> ReadAsync(int tenderId, string userId, string sharedLink)
    {
        var tender = await _db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.CommitteeMembers).Include(t => t.EvaluationCriteria)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations)
            .SingleOrDefaultAsync(t => t.Id == tenderId && t.PublishDate != null
                && t.CommitteeMembers.Any(m => m.UserId == userId));
        if (tender == null || !await _db.Notifications.AnyAsync(n => n.UserId == userId
            && n.Type == "TenderEvaluation" && n.Link == sharedLink)) return null;
        var state = new CommitteeRecommendationViewModel { TenderId = tenderId };
        state.IsSubmitted = await _db.EvaluationReports.AnyAsync(r => r.TenderId == tenderId);
        var members = tender.CommitteeMembers.Select(m => m.UserId).Distinct().ToList();
        var confirmations = await _db.EvaluationCommitteeSubmissions.AsNoTracking()
            .Where(s => s.TenderId == tenderId && members.Contains(s.EvaluatorUserId)).ToListAsync();
        state.ReadyMembers = confirmations.Count;
        state.IsReady = confirmations.Any(s => s.EvaluatorUserId == userId);
        state.AllReady = members.Count == 4 && confirmations.Count == 4;
        state.VotingRound = confirmations.Select(s => s.VotingRound).DefaultIfEmpty(1).Max();
        state.VotedMembers = confirmations.Count(s => s.VotingRound == state.VotingRound && s.VotedAt != null && s.RecommendedBidId != null);
        state.MyRecommendedBidId = confirmations.FirstOrDefault(s => s.EvaluatorUserId == userId)?.RecommendedBidId;
        var offers = tender.Bids.Where(b => b.SubmittedAt != null
            && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted)).ToList();
        var criteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).ToList();
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        if (members.Count != 4 || tender.CommitteeMembers.Count != 4
            || seats.Any(seat => tender.CommitteeMembers.Count(m => m.MemberRole == seat) != 1)
            || criteria.Count == 0 || criteria.Sum(c => c.WeightPercent) is <= 0 or > 80
            || tender.EvaluationCriteria.Count(c => c.Type == Statuses.CriterionType.Financial) != 1
            || tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent) != 20
            || tender.EvaluationCriteria.Any(c => !c.IsLocked || c.MaxScore <= 0 || c.WeightPercent <= 0)
            || tender.EvaluationCriteria.Any(c => c.Type != Statuses.CriterionType.Technical && c.Type != Statuses.CriterionType.Financial)
            || offers.Count == 0 || offers.Any(b => b.TotalPrice <= 0 || string.IsNullOrWhiteSpace(b.Currency))
            || offers.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1
            || tender.Bids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview)
            || (tender.Status != Statuses.TenderStatus.Published && tender.Status != Statuses.TenderStatus.Open
                && tender.Status != Statuses.TenderStatus.Closed && tender.Status != Statuses.TenderStatus.UnderEvaluation))
            state.ConfigurationMessage = "Readiness is waiting for a valid locked rubric, four assigned committee seats and completed compliance with comparable prices.";
        var prices = offers.Select(b => b.TotalPrice).Distinct().OrderBy(p => p).ToList();
        foreach (var offer in offers.OrderBy(b => b.SupplierProfile.CompanyName))
        {
            var row = new RecommendationOfferViewModel { BidId = offer.Id, Supplier = offer.SupplierProfile.CompanyName };
            if (state.ConfigurationMessage == null)
                row.FinancialPoints = AssessmentsController.CalculateFinancialPoints(prices, offer.TotalPrice, 20);
            var points = new List<decimal>();
            foreach (var member in members)
            {
                var scores = criteria.Select(c => new EvaluationScoreViewModel { MaxScore = c.MaxScore, WeightPercent = c.WeightPercent,
                    Score = offer.TechnicalEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member && e.CriterionId == c.Id)?.Score }).ToList();
                var technical = AssessmentsController.CalculateTechnicalPoints(scores);
                var saved = offer.FinancialEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member);
                var complete = technical.HasValue && saved?.CompletedAt != null
                    && saved.TotalCost == offer.TotalPrice && row.FinancialPoints.HasValue
                    && saved.Score == decimal.Round(row.FinancialPoints.Value, 2)
                    && offer.TechnicalEvaluations.Where(e => e.EvaluatorUserId == member && criteria.Any(c => c.Id == e.CriterionId))
                        .All(e => e.EvaluatedAt <= saved.CompletedAt && e.Score == decimal.Round(e.Score!.Value, 2));
                if (member == userId) { row.MyTechnicalPoints = technical; row.HasSavedEvaluation = complete; }
                if (complete) points.Add(technical!.Value);
            }
            if (state.AllReady && points.Count == 4) row.CommitteeTotal = decimal.Round(points.Average() + row.FinancialPoints!.Value, 2);
            state.Offers.Add(row);
        }
        state.CanReady = !state.IsSubmitted && !state.IsReady && state.ConfigurationMessage == null
            && state.Offers.Count > 0 && state.Offers.All(o => o.HasSavedEvaluation);
        return state;
    }

    // A unique vote leader wins. Only tied vote leaders are compared by their displayed final committee total.
    public static int? ResolveWinner(IReadOnlyList<EvaluationCommitteeSubmission> votes, IReadOnlyList<RecommendationOfferViewModel> offers)
    {
        if (votes.Count != 4 || votes.Select(v => v.EvaluatorUserId).Distinct().Count() != 4
            || votes.Select(v => v.VotingRound).Distinct().Count() != 1
            || votes.Any(v => v.VotedAt == null || v.RecommendedBidId == null
                || !offers.Any(o => o.BidId == v.RecommendedBidId && o.CommitteeTotal.HasValue))) return null;
        var counts = votes.GroupBy(v => v.RecommendedBidId!.Value).Select(g => new { BidId = g.Key, Count = g.Count() }).ToList();
        var leaders = counts.Where(c => c.Count == counts.Max(v => v.Count)).ToList();
        if (leaders.Count == 1) return leaders[0].BidId;
        var tied = offers.Where(o => leaders.Any(l => l.BidId == o.BidId)).ToList();
        var best = tied.Where(o => o.CommitteeTotal == tied.Max(t => t.CommitteeTotal)).ToList();
        return best.Count == 1 ? best[0].BidId : null;
    }
}
