using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Areas.Evaluation.Services;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class AssessmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly EvaluationReportService _reports;

    public AssessmentsController(ApplicationDbContext db, AuditService audit, EvaluationReportService reports)
    {
        _db = db;
        _audit = audit;
        _reports = reports;
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? evaluatorId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var model = await BuildFormAsync(id, userId, evaluatorId ?? userId);
        if (model == null) return NotFound();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Save([Bind(Prefix = "Input")] SaveEvaluationViewModel input)
        => SaveFormAsync(input, false, false);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SaveDraft([Bind(Prefix = "Input")] SaveEvaluationViewModel input)
        => SaveFormAsync(input, true, false);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Ready([Bind(Prefix = "Input")] SaveEvaluationViewModel input, bool confirmReady)
    {
        if (!confirmReady) return Task.FromResult<IActionResult>(BadRequest("Confirm Ready before finalizing your form."));
        return SaveFormAsync(input, false, true);
    }

    private async Task<IActionResult> SaveFormAsync(SaveEvaluationViewModel input, bool isDraft, bool markReady)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var tenderId = await _db.Bids.AsNoTracking().Where(b => b.Id == input.BidId
            && b.Tender.CommitteeMembers.Any(m => m.UserId == userId)).Select(b => (int?)b.TenderId).SingleOrDefaultAsync();
        if (!tenderId.HasValue) return NotFound();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        // All Evaluation saves for this tender take the same lock before reading readiness.
        // This serializes simultaneous final Ready clicks and prevents duplicate reports.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Tenders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {tenderId.Value}");
        var model = await BuildFormAsync(input.BidId, userId, userId);
        if (model == null) return NotFound();
        if (model.ReadyAt.HasValue)
        {
            if (markReady) return RedirectToAction(nameof(Edit), new { id = input.BidId });
            return Conflict("Your form is already Ready and cannot be edited.");
        }
        if (model.IsSubmitted) return Conflict("The report has already been sent to the Sourcing Officer.");
        if (model.ConfigurationMessage != null) ModelState.AddModelError("", model.ConfigurationMessage);
        if (!isDraft && model.FinancialMessage != null) ModelState.AddModelError("", model.FinancialMessage);
        input.Scores ??= new List<EvaluationScoreViewModel>();
        var expectedIds = model.Input.Scores.Select(c => c.CriterionId).OrderBy(id => id).ToList();
        var submittedIds = input.Scores.Select(c => c.CriterionId).OrderBy(id => id).ToList();
        if (!expectedIds.SequenceEqual(submittedIds)) return BadRequest("The submitted criteria do not match this tender. Reload the form.");
        for (var i = 0; i < model.Input.Scores.Count; i++)
        {
            var criterion = model.Input.Scores[i];
            var entered = input.Scores.Single(c => c.CriterionId == criterion.CriterionId);
            criterion.Score = entered.Score;
            criterion.Notes = entered.Notes;
            if ((!isDraft && !entered.Score.HasValue) || (entered.Score.HasValue
                && (entered.Score < 0 || entered.Score > criterion.MaxScore || entered.Score != decimal.Round(entered.Score.Value, 2))))
                ModelState.AddModelError("Input.Scores[" + i + "].Score", "Enter a score from 0 to " + criterion.MaxScore + " with at most two decimal places.");
            if (entered.Notes?.Length > 1000) ModelState.AddModelError("", "Observations cannot exceed 1,000 characters.");
        }
        model.Input.FinancialNotes = input.FinancialNotes;
        if (input.FinancialNotes?.Length > 1000) ModelState.AddModelError("Input.FinancialNotes", "Observations cannot exceed 1,000 characters.");
        if (!ModelState.IsValid)
        {
            model.TechnicalPoints = CalculateTechnicalPoints(model.Input.Scores);
            return View("Edit", model);
        }
        var now = DateTime.UtcNow;
        var savedScores = await _db.TechnicalEvaluations.Where(e => e.BidId == input.BidId && e.EvaluatorUserId == userId).ToListAsync();
        foreach (var criterion in model.Input.Scores)
        {
            var evaluation = savedScores.SingleOrDefault(e => e.CriterionId == criterion.CriterionId);
            if (evaluation == null)
            {
                evaluation = new TechnicalEvaluation { BidId = input.BidId, CriterionId = criterion.CriterionId, EvaluatorUserId = userId };
                _db.TechnicalEvaluations.Add(evaluation);
            }
            evaluation.Score = criterion.Score;
            evaluation.Notes = criterion.Notes?.Trim();
            evaluation.EvaluatedAt = now;
        }
        var financial = await _db.FinancialEvaluations.SingleOrDefaultAsync(e => e.BidId == input.BidId && e.EvaluatorUserId == userId);
        if (financial == null)
        {
            financial = new FinancialEvaluation { BidId = input.BidId, EvaluatorUserId = userId };
            _db.FinancialEvaluations.Add(financial);
        }
        financial.Score = model.FinancialPoints.HasValue ? decimal.Round(model.FinancialPoints.Value, 2) : null;
        financial.TotalCost = model.TotalPrice;
        financial.TaxesAndFees = model.Items.Sum(i => i.TaxAmount);
        financial.LogisticsCost = model.Items.Sum(i => i.ShippingCost + i.InstallationCost);
        financial.Notes = input.FinancialNotes?.Trim();
        financial.EvaluatedAt = now;
        if (markReady) financial.ReadyAt = now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(markReady ? "EvaluationReady" : (isDraft ? "EvaluationDraftSaved" : "Evaluated"),
            "Bid", input.BidId.ToString(), markReady ? "Member confirmed this offer as final." : "Saved without final confirmation.");
        var reportCreated = false;
        if (markReady)
        {
            var link = Url.Action("Details", "Tenders", new { area = "Sourcing", id = model.TenderId, tab = "ranking" })
                ?? throw new InvalidOperationException("Could not generate Sourcing report link.");
            reportCreated = await _reports.TryCreateAsync(model.TenderId, userId, link);
        }
        await transaction.CommitAsync();
        TempData["EvaluationSaved"] = reportCreated ? "All required forms are Ready. The results report was sent to the Sourcing Officer."
            : markReady ? "Your form is Ready and locked. The report will be sent when all four members confirm every eligible offer."
            : isDraft ? "Draft saved. You can return and complete or edit it later."
            : "Evaluation saved. You can edit it until you confirm Ready.";
        if (reportCreated) return RedirectToAction("Index", "Report", new { id = model.TenderId });
        return RedirectToAction(nameof(Edit), new { id = input.BidId });
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var bidId = await _db.BidDocuments.AsNoTracking().Where(d => d.Id == id)
            .Select(d => (int?)d.BidId).SingleOrDefaultAsync();
        if (bidId == null || await BuildFormAsync(bidId.Value, userId, userId) == null) return NotFound();
        // Reuse the existing protected download and document access logging.
        return RedirectToAction("DownloadOfferDocument", "CommitteeBrief", new { area = "Sourcing", id });
    }

    private async Task<EvaluationFormViewModel?> BuildFormAsync(int bidId, string userId, string evaluatorId)
    {
        var bid = await _db.Bids.AsNoTracking()
            .Include(b => b.SupplierProfile)
            .Include(b => b.BidItems).ThenInclude(i => i.RequisitionItem)
            .Where(b => b.Id == bidId && b.SubmittedAt != null
                && b.Tender.PublishDate != null
                && (b.Tender.Status == Statuses.TenderStatus.Published || b.Tender.Status == Statuses.TenderStatus.Open
                    || b.Tender.Status == Statuses.TenderStatus.Closed || b.Tender.Status == Statuses.TenderStatus.UnderEvaluation)
                && b.Tender.CommitteeMembers.Any(m => m.UserId == userId)
                && b.Tender.CommitteeMembers.Any(m => m.UserId == evaluatorId)
                && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
            .SingleOrDefaultAsync();
        if (bid == null) return null;
        var sharedLink = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id = bid.TenderId });
        if (!await _db.Notifications.AnyAsync(n => n.UserId == userId && n.Type == "TenderEvaluation"
            && n.Link == sharedLink)) return null;

        var tender = await _db.Tenders.AsNoTracking().SingleAsync(t => t.Id == bid.TenderId);
        var criteria = await _db.EvaluationCriteria.AsNoTracking().Where(c => c.TenderId == bid.TenderId).OrderBy(c => c.Id).ToListAsync();
        var allScores = await _db.TechnicalEvaluations.AsNoTracking().Where(e => e.BidId == bidId).ToListAsync();
        var scores = allScores.Where(e => e.EvaluatorUserId == evaluatorId)
            .OrderByDescending(e => e.EvaluatedAt).ThenByDescending(e => e.Id).ToList();
        var financial = await _db.FinancialEvaluations.AsNoTracking().SingleOrDefaultAsync(e => e.BidId == bidId && e.EvaluatorUserId == evaluatorId);
        var model = new EvaluationFormViewModel
        {
            TenderId = bid.TenderId, TenderNumber = tender.TenderNumber,
            CompanyName = bid.SupplierProfile.CompanyName, IsReadOnly = userId != evaluatorId,
            TotalPrice = bid.TotalPrice, Currency = bid.Currency, PaymentTerms = bid.PaymentTerms,
            DeliveryDays = bid.DeliveryDays, ValidityDays = bid.ValidityDays, TechnicalSummary = bid.TechnicalSummary,
            Input = new SaveEvaluationViewModel { BidId = bidId, FinancialNotes = financial?.Notes },
            SavedFinancialPoints = financial?.Score
        };
        model.IsSubmitted = await _db.EvaluationReports.AnyAsync(r => r.TenderId == bid.TenderId && r.SubmittedAt != null);
        model.ReadyAt = financial?.ReadyAt;
        model.IsReadOnly = model.IsReadOnly || model.IsSubmitted || model.ReadyAt.HasValue;
        model.TechnicalWeight = criteria.Where(c => c.Type == Statuses.CriterionType.Technical).Sum(c => c.WeightPercent);
        model.FinancialWeight = criteria.Where(c => c.Type == Statuses.CriterionType.Financial).Sum(c => c.WeightPercent);
        foreach (var criterion in criteria.Where(c => c.Type == Statuses.CriterionType.Technical))
        {
            var saved = scores.FirstOrDefault(e => e.CriterionId == criterion.Id);
            model.Input.Scores.Add(new EvaluationScoreViewModel
            {
                CriterionId = criterion.Id, Name = criterion.Name, MaxScore = criterion.MaxScore,
                WeightPercent = criterion.WeightPercent, Score = saved?.Score, Notes = saved?.Notes
            });
        }

        model.Members = await _db.CommitteeMembers.AsNoTracking().Where(m => m.TenderId == bid.TenderId)
            .OrderBy(m => m.Id).Select(m => new EvaluationMemberViewModel
            {
                UserId = m.UserId, Name = m.User.FullName, Role = m.MemberRole,
                HasSavedScores = _db.TechnicalEvaluations.Any(e => e.BidId == bidId && e.EvaluatorUserId == m.UserId)
            }).ToListAsync();
        var confirmations = await _db.FinancialEvaluations.AsNoTracking().Where(e => e.BidId == bidId).ToListAsync();
        foreach (var member in model.Members)
        {
            member.ReadyAt = confirmations.SingleOrDefault(e => e.EvaluatorUserId == member.UserId)?.ReadyAt;
            member.IsCurrentUser = member.UserId == userId;
            member.IsSelected = member.UserId == evaluatorId;
            var memberScores = new List<EvaluationScoreViewModel>();
            foreach (var criterion in model.Input.Scores)
            {
                var saved = allScores.Where(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.CriterionId)
                    .OrderByDescending(e => e.EvaluatedAt).ThenByDescending(e => e.Id).FirstOrDefault();
                memberScores.Add(new EvaluationScoreViewModel
                {
                    Score = saved?.Score, MaxScore = criterion.MaxScore, WeightPercent = criterion.WeightPercent
                });
            }
            member.TechnicalPoints = CalculateTechnicalPoints(memberScores);
        }
        model.SavedMemberCount = model.Members.Count(m => m.TechnicalPoints.HasValue);
        model.EvaluatorName = model.Members.First(m => m.UserId == evaluatorId).Name;
        model.Documents = await _db.BidDocuments.AsNoTracking().Where(d => d.BidId == bidId)
            .OrderBy(d => d.Id).Select(d => new EvaluationDocumentViewModel
            {
                Id = d.Id, FileName = d.FileName, DocType = d.DocType
            }).ToListAsync();
        if (model.Members.Select(m => m.UserId).Distinct().Count() != 4
            || model.Input.Scores.Count != criteria.Count(c => c.Type == Statuses.CriterionType.Technical) || criteria.Any(c => !c.IsLocked || c.MaxScore <= 0 || c.WeightPercent <= 0)
            || model.TechnicalWeight is <= 0 or > 80 || model.FinancialWeight != 20
            || criteria.Any(c => c.Type != Statuses.CriterionType.Technical && c.Type != Statuses.CriterionType.Financial))
            model.ConfigurationMessage = "This tender must use Sourcing’s current rubric: locked technical questions totaling up to 80% and financial criteria totaling 20%, with four different committee members. Older demo rubrics cannot be evaluated.";

        model.TechnicalPoints = CalculateTechnicalPoints(model.Input.Scores);
        if (model.ConfigurationMessage == null && model.Members.Count == 4 && model.SavedMemberCount == 4)
            model.AverageSavedTechnicalPoints = decimal.Round(model.Members.Average(m => m.TechnicalPoints!.Value), 2);

        foreach (var item in bid.BidItems)
        {
            model.Items.Add(new EvaluationItemViewModel
            {
                Name = item.RequisitionItem.ItemName, RequiredSpecifications = item.RequisitionItem.Specifications,
                OfferedSpecifications = item.OfferedSpecifications, RequiredQuantity = item.RequisitionItem.Quantity,
                Quantity = item.Quantity, UnitPrice = item.UnitPrice, TaxAmount = item.TaxAmount,
                ShippingCost = item.ShippingCost, InstallationCost = item.InstallationCost ?? 0
            });
        }

        // Compare supplier totals without adding taxes or shipping a second time.
        var comparisonBids = await _db.Bids.AsNoTracking().Where(b => b.TenderId == bid.TenderId)
            .Select(b => new { b.Id, b.Status, b.SupplierProfile.CompanyName, b.DeliveryDays, b.TotalPrice, b.Currency })
            .ToListAsync();
        var eligible = comparisonBids.Where(b => b.Status == Statuses.BidStatus.Compliant
            || b.Status == Statuses.BidStatus.Accepted).ToList();
        if (comparisonBids.Any(b => b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview))
            model.FinancialMessage = "Financial comparison is waiting for all submitted offers to complete compliance review.";
        else if (eligible.Count == 0 || eligible.Any(b => b.TotalPrice <= 0 || string.IsNullOrWhiteSpace(b.Currency))
            || eligible.Select(b => b.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            model.FinancialMessage = "Financial comparison requires positive supplier totals and one currency. Contact Sourcing.";
        var prices = eligible.Select(b => b.TotalPrice).Distinct().OrderBy(price => price).ToList();
        if (model.FinancialMessage == null && model.ConfigurationMessage == null)
        {
            model.IsLowestPrice = bid.TotalPrice == prices[0];
            model.FinancialPoints = CalculateFinancialPoints(prices, bid.TotalPrice, model.FinancialWeight);
        }
        var myTechnicalScores = await _db.TechnicalEvaluations.AsNoTracking()
            .Where(e => e.Bid.TenderId == bid.TenderId && e.EvaluatorUserId == userId).ToListAsync();
        var myConfirmations = await _db.FinancialEvaluations.AsNoTracking()
            .Where(e => e.Bid.TenderId == bid.TenderId && e.EvaluatorUserId == userId).ToListAsync();
        foreach (var offer in eligible.OrderBy(b => b.CompanyName))
        {
            model.ComparisonOffers.Add(new EvaluationOfferViewModel
            {
                Id = offer.Id, CompanyName = offer.CompanyName, TotalPrice = offer.TotalPrice,
                Currency = offer.Currency, DeliveryDays = offer.DeliveryDays,
                IsReady = myConfirmations.Any(e => e.BidId == offer.Id && e.ReadyAt != null),
                IsLowestPrice = model.FinancialMessage == null ? offer.TotalPrice == eligible.Min(b => b.TotalPrice) : null,
                HasSavedEvaluation = model.ConfigurationMessage == null && model.FinancialMessage == null
                    && model.Input.Scores.All(criterion => myTechnicalScores.Any(e => e.BidId == offer.Id
                        && e.CriterionId == criterion.CriterionId && e.Score >= 0 && e.Score <= criterion.MaxScore))
            });
        }
        return model;
    }

    internal static decimal? CalculateFinancialPoints(List<decimal> prices, decimal total, decimal weight)
    {
        var orderedPrices = prices.Distinct().OrderBy(price => price).ToList();
        var rank = orderedPrices.IndexOf(total);
        if (rank < 0 || weight <= 0 || orderedPrices.Any(price => price <= 0)) return null;
        return weight * (orderedPrices.Count - rank) / orderedPrices.Count;
    }

    internal static decimal? CalculateTechnicalPoints(List<EvaluationScoreViewModel> scores)
    {
        if (scores.Count == 0) return null;
        decimal total = 0;
        foreach (var criterion in scores)
        {
            if (!criterion.Score.HasValue || criterion.MaxScore <= 0
                || criterion.Score < 0 || criterion.Score > criterion.MaxScore) return null;
            total += criterion.Score.Value / criterion.MaxScore * criterion.WeightPercent;
        }
        return total;
    }
}
