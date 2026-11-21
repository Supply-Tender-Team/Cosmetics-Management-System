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
        var existing = await _db.EvaluationReports.AsNoTracking().FirstOrDefaultAsync(r => r.TenderId == id, ct);
        // Historical submitted reports used a single automatic price rule, not manually scored financial questions.
        // Read their saved subtotal; never recalculate old results using today's prices or invent missing new answers.
        var financialCriteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).ToList();
        var legacyPriceReport = existing?.SubmittedAt != null && financialCriteria.Count == 1
            && financialCriteria[0].Name == "Eligible price ranking" && financialCriteria[0].MaxScore == 1
            && !tender.Bids.Any(b => b.TechnicalEvaluations.Any(e => e.CriterionId == financialCriteria[0].Id));
        var criteria = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Technical).OrderBy(c => c.Id).ToList();
        var members = tender.CommitteeMembers.OrderBy(m => m.Id).ToList();
        var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial,
            Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
        var eligible = tender.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted).ToList();
        model.TechnicalWeight = criteria.Sum(c => c.WeightPercent);
        model.FinancialWeight = tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent);
        var validRubric = EvaluationScoring.ValidRubric(tender.EvaluationCriteria)
            && members.Count == 4 && members.Select(m => m.UserId).Distinct().Count() == 4
            && seats.All(seat => members.Count(m => m.MemberRole == seat) == 1);
        var comparable = eligible.Count > 0 && eligible.All(b => b.TotalPrice > 0 && !string.IsNullOrWhiteSpace(b.Currency))
            && eligible.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && !tender.Bids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview);
        foreach (var bid in tender.Bids.Where(b => b.Status != Statuses.BidStatus.Draft).OrderBy(b => b.TotalPrice))
        {
            var row = new ReportOfferViewModel { BidId = bid.Id, Supplier = bid.SupplierProfile.CompanyName,
                Status = bid.Status, Eligible = eligible.Any(b => b.Id == bid.Id), Price = bid.TotalPrice, Currency = bid.Currency };
            if (row.Eligible && validRubric)
            {
                foreach (var member in members)
                {
                    var detail = new ReportMemberViewModel { Name = member.User.FullName };
                    foreach (var criterion in tender.EvaluationCriteria.OrderBy(c => c.Id))
                    {
                        var saved = bid.TechnicalEvaluations.Where(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.Id)
                            .OrderByDescending(e => e.EvaluatedAt).ThenByDescending(e => e.Id).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(saved?.Notes))
                        {
                            var note = saved.Notes.Trim();
                            if (!detail.Comments.ContainsKey(note)) detail.Comments.Add(note, new List<string>());
                            detail.Comments[note].Add(criterion.Name);
                        }
                    }
                    var memberLabel = member.MemberRole + " — " + member.User.FullName;
                    detail.Points = EvaluationScoring.Points(criteria, bid.TechnicalEvaluations, member.UserId);
                    detail.FinancialPoints = EvaluationScoring.Points(tender.EvaluationCriteria.Where(c => c.Type == Statuses.CriterionType.Financial), bid.TechnicalEvaluations, member.UserId);
                    row.MemberPoints.Add(memberLabel, detail.Points);
                    var confirmation = bid.FinancialEvaluations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId);
                    if (legacyPriceReport && confirmation?.ReadyAt != null && confirmation.Score >= 0
                        && confirmation.Score <= model.FinancialWeight)
                        detail.FinancialPoints = confirmation.Score;
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
                if (row.ReadyMemberCount == 4 && row.Members.All(m => m.FinancialPoints.HasValue))
                    row.FinancialPoints = row.Members.Average(m => m.FinancialPoints!.Value);
            }
            model.Offers.Add(row);
        }
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
            && model.Offers.Where(o => o.Eligible).All(o => o.ReadyMemberCount == 4 && o.TechnicalPoints.HasValue && o.FinancialPoints.HasValue);
        if (!model.SubmittedAt.HasValue)
            model.PendingMessage = "Pending: each member saves all eligible-offer evaluations and confirms Ready once. After all four recommendation choices are confirmed, the report is sent automatically when one recommended offer is resolved.";
        if (members.Any(m => m.UserId == userId)) model.AiItems = await _ai.GetLatestAsync(id.Value, userId, ct);
        return model;
    }
}
