using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Controllers;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Areas.Evaluation.Services;

public class EvaluationReportReader
{
    private readonly ApplicationDbContext _db;
    private readonly AiBidAnalysisService _ai;

    public EvaluationReportReader(ApplicationDbContext db, AiBidAnalysisService ai)
    {
        _db = db;
        _ai = ai;
    }

    public async Task<UnifiedReportViewModel?> BuildAsync(int? id, string userId, bool isOfficer, IUrlHelper url, CancellationToken ct)
    {
        var tenders = await _db.Tenders.AsNoTracking()
            .Where(t => t.PublishDate != null && t.Status != Statuses.TenderStatus.Draft
                && (t.CommitteeMembers.Any(m => m.UserId == userId) || (isOfficer && t.CreatedByUserId == userId)))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new EvaluationTenderViewModel { Id = t.Id, TenderNumber = t.TenderNumber, Title = t.Title })
            .ToListAsync(ct);
        var links = await _db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Type == "TenderEvaluation")
            .Select(n => n.Link).ToListAsync(ct);
        var officerTenderIds = isOfficer ? await _db.EvaluationReports.Where(r => r.Tender.CreatedByUserId == userId)
            .Select(r => r.TenderId).ToListAsync(ct) : new List<int>();
        tenders = tenders.Where(t => officerTenderIds.Contains(t.Id) || links.Contains(url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();
        var model = new UnifiedReportViewModel { Tenders = tenders, TenderId = id };
        if (!id.HasValue) return model;
        var selected = tenders.FirstOrDefault(t => t.Id == id);
        if (selected == null) return null;
        model.TenderNumber = selected.TenderNumber;
        model.TenderTitle = selected.Title;
        var tender = await _db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.CommitteeMembers).ThenInclude(m => m.User)
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.Bids).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations)
            .SingleAsync(t => t.Id == id, ct);
        var criteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).OrderBy(c => c.Id).ToList();
        var members = tender.CommitteeMembers.OrderBy(m => m.Id).ToList();
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        var eligible = tender.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted).ToList();
        model.TechnicalWeight = criteria.Sum(c => c.WeightPercent);
        var validRubric = model.TechnicalWeight is > 0 and <= 80
            && tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent) == 20
            && tender.EvaluationCriteria.All(c => c.IsLocked && c.MaxScore > 0 && c.WeightPercent > 0)
            && members.Count == 4 && members.Select(m => m.UserId).Distinct().Count() == 4
            && seats.All(seat => members.Count(m => m.MemberRole == seat) == 1)
            && tender.EvaluationCriteria.Count(c => c.Type == Statuses.CriterionType.Financial) == 1;
        var comparable = eligible.Count > 0 && eligible.All(b => b.TotalPrice > 0 && !string.IsNullOrWhiteSpace(b.Currency))
            && eligible.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && !tender.Bids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview);
        var prices = eligible.Select(b => b.TotalPrice).Distinct().OrderBy(p => p).ToList();
        foreach (var bid in tender.Bids.Where(b => b.Status != Statuses.BidStatus.Draft).OrderBy(b => b.TotalPrice))
        {
            var row = new ReportOfferViewModel { BidId = bid.Id, Supplier = bid.SupplierProfile.CompanyName,
                Status = bid.Status, Eligible = eligible.Any(b => b.Id == bid.Id), Price = bid.TotalPrice, Currency = bid.Currency };
            if (row.Eligible && validRubric)
            {
                foreach (var member in members)
                {
                    var detail = new ReportMemberViewModel { Name = member.User.FullName };
                    var scores = new List<EvaluationScoreViewModel>();
                    foreach (var criterion in criteria)
                    {
                        var saved = bid.TechnicalEvaluations.Where(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.Id)
                            .OrderByDescending(e => e.EvaluatedAt).ThenByDescending(e => e.Id).FirstOrDefault();
                        scores.Add(new EvaluationScoreViewModel { Score = saved?.Score, MaxScore = criterion.MaxScore, WeightPercent = criterion.WeightPercent });
                        if (!string.IsNullOrWhiteSpace(saved?.Notes))
                        {
                            var note = saved.Notes.Trim();
                            if (!detail.Comments.ContainsKey(note)) detail.Comments.Add(note, new List<string>());
                            detail.Comments[note].Add(criterion.Name);
                        }
                    }
                    var memberLabel = member.MemberRole + " — " + member.User.FullName;
                    detail.Points = AssessmentsController.CalculateTechnicalPoints(scores);
                    row.MemberPoints.Add(memberLabel, detail.Points);
                    var confirmation = bid.FinancialEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId);
                    detail.Ready = confirmation?.ReadyAt != null;
                    row.ReadyMembers.Add(memberLabel, detail.Ready);
                    if (confirmation?.ReadyAt != null) row.ReadyMemberCount++;
                    var financialNote = bid.FinancialEvaluations.Where(e => e.EvaluatorUserId == member.UserId)
                        .OrderByDescending(e => e.EvaluatedAt).FirstOrDefault()?.Notes;
                    if (!string.IsNullOrWhiteSpace(financialNote))
                    {
                        var note = financialNote.Trim();
                        if (!detail.Comments.ContainsKey(note)) detail.Comments.Add(note, new List<string>());
                        detail.Comments[note].Add("Financial assessment");
                    }
                    row.Members.Add(detail);
                }
                if (row.ReadyMemberCount == 4 && row.MemberPoints.Values.All(p => p.HasValue)) row.TechnicalPoints = row.MemberPoints.Values.Average();
                if (comparable) row.FinancialPoints = AssessmentsController.CalculateFinancialPoints(prices, bid.TotalPrice, 20);
            }
            model.Offers.Add(row);
        }
        var existing = await _db.EvaluationReports.AsNoTracking().FirstOrDefaultAsync(r => r.TenderId == id, ct);
        model.SubmittedAt = existing?.SubmittedAt;
        model.TechnicalSnapshot = existing?.TechnicalSummary ?? string.Empty;
        model.FinancialSnapshot = existing?.FinancialSummary ?? string.Empty;
        model.SubmittedJustification = existing?.Justification ?? string.Empty;
        model.RecommendedSupplier = model.Offers.FirstOrDefault(o => o.BidId == existing?.RecommendedBidId)?.Supplier;
        var activeStage = tender.Status == Statuses.TenderStatus.Published || tender.Status == Statuses.TenderStatus.Open
            || tender.Status == Statuses.TenderStatus.Closed || tender.Status == Statuses.TenderStatus.UnderEvaluation;
        model.CanOpenOffers = activeStage && members.Any(m => m.UserId == userId);
        model.ReadyFormCount = model.Offers.Where(o => o.Eligible).Sum(o => o.ReadyMemberCount);
        model.ReadyMemberCount = eligible.Count == 0 ? 0 : members.Count(m => eligible.All(b =>
            b.FinancialEvaluations.Any(e => e.EvaluatorUserId == m.UserId && e.ReadyAt != null)));
        model.RequiredFormCount = eligible.Count * 4;
        model.AllReady = activeStage && validRubric && comparable && eligible.Count > 0
            && model.Offers.Where(o => o.Eligible).All(o => o.ReadyMemberCount == 4 && o.TechnicalPoints.HasValue);
        if (!model.SubmittedAt.HasValue)
            model.PendingMessage = "Pending: each member saves all eligible-offer evaluations and confirms Ready once. After all four recommendation choices are confirmed, the report is sent automatically when one recommended offer is resolved.";
        if (members.Any(m => m.UserId == userId)) model.AiItems = await _ai.GetLatestAsync(id.Value, userId, ct);
        return model;
    }
}
