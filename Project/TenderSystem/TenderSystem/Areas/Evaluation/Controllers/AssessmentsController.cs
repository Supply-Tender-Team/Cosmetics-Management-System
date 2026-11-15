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

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee)]
public class AssessmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;

    public AssessmentsController(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
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
    public async Task<IActionResult> Save([Bind(Prefix = "Input")] SaveEvaluationViewModel input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();

        // Recheck membership, compliance, criteria and prices in the same transaction as the save.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var model = await BuildFormAsync(input.BidId, userId, userId);
        if (model == null) return NotFound();
        if (model.ConfigurationMessage != null) ModelState.AddModelError("", model.ConfigurationMessage);
        if (model.FinancialMessage != null) ModelState.AddModelError("", model.FinancialMessage);

        var expectedIds = model.Input.Scores.Select(s => s.CriterionId).OrderBy(id => id).ToList();
        var submittedIds = input.Scores.Select(s => s.CriterionId).OrderBy(id => id).ToList();
        if (!expectedIds.SequenceEqual(submittedIds))
            return BadRequest("The submitted criteria do not match this tender. Reload the form.");

        for (var i = 0; i < model.Input.Scores.Count; i++)
        {
            var criterion = model.Input.Scores[i];
            var entered = input.Scores.FirstOrDefault(s => s.CriterionId == criterion.CriterionId);
            if (entered == null) continue;
            criterion.Score = entered.Score;
            criterion.Notes = entered.Notes;
            if (!entered.Score.HasValue || entered.Score < 0 || entered.Score > criterion.MaxScore
                || entered.Score != decimal.Round(entered.Score.Value, 2))
                ModelState.AddModelError("", "Enter a score from 0 to " + criterion.MaxScore + " for " + criterion.Name + ", using at most two decimal places.");
        }
        model.Input.FinancialNotes = input.FinancialNotes;

        if (!ModelState.IsValid)
        {
            model.TechnicalPoints = null;
            return View("Edit", model);
        }

        var savedScores = await _db.TechnicalEvaluations.Where(e => e.BidId == input.BidId
            && e.EvaluatorUserId == userId).ToListAsync();
        foreach (var criterion in model.Input.Scores)
        {
            var evaluation = savedScores.FirstOrDefault(e => e.CriterionId == criterion.CriterionId);
            if (evaluation == null)
            {
                evaluation = new TechnicalEvaluation
                {
                    BidId = input.BidId, CriterionId = criterion.CriterionId, EvaluatorUserId = userId
                };
                _db.TechnicalEvaluations.Add(evaluation);
            }
            evaluation.Score = criterion.Score!.Value;
            evaluation.Notes = criterion.Notes?.Trim();
            evaluation.EvaluatedAt = DateTime.UtcNow;
        }

        var financial = await _db.FinancialEvaluations.SingleOrDefaultAsync(e =>
            e.BidId == input.BidId && e.EvaluatorUserId == userId);
        if (financial == null)
        {
            financial = new FinancialEvaluation { BidId = input.BidId, EvaluatorUserId = userId };
            _db.FinancialEvaluations.Add(financial);
        }
        financial.Score = model.FinancialPoints!.Value;
        financial.TotalCost = model.TotalPrice;
        financial.TaxesAndFees = model.Items.Sum(i => i.TaxAmount);
        financial.LogisticsCost = model.Items.Sum(i => i.ShippingCost + i.InstallationCost);
        financial.Notes = input.FinancialNotes?.Trim();
        financial.EvaluatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("Evaluated", "Bid", input.BidId.ToString(), "Member technical and financial assessment saved; not a final submission.");
        await transaction.CommitAsync();
        var nextOffer = model.ComparisonOffers.FirstOrDefault(o => o.Id != input.BidId && !o.HasSavedEvaluation);
        if (nextOffer != null)
        {
            TempData["EvaluationSaved"] = "Your evaluation for " + model.CompanyName + " was saved. The next offer is open below.";
            return RedirectToAction(nameof(Edit), new { id = nextOffer.Id });
        }
        TempData["EvaluationSaved"] = "Thank you. Your evaluations for all current eligible offers are saved. They remain editable; final submission is not enabled yet.";
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
        return RedirectToAction("Download", "Offers", new { area = "Supplier", documentId = id });
    }

    private async Task<EvaluationFormViewModel?> BuildFormAsync(int bidId, string userId, string evaluatorId)
    {
        var bid = await _db.Bids.AsNoTracking()
            .Include(b => b.SupplierProfile)
            .Include(b => b.BidItems).ThenInclude(i => i.RequisitionItem)
            .Where(b => b.Id == bidId && b.SubmittedAt != null
                && b.Tender.Status == Statuses.TenderStatus.UnderEvaluation
                && b.Tender.CommitteeMembers.Any(m => m.UserId == userId)
                && b.Tender.CommitteeMembers.Any(m => m.UserId == evaluatorId)
                && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Submitted || b.Status == Statuses.BidStatus.UnderReview)
                && b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                    .Select(c => c.Result).FirstOrDefault() == Statuses.ComplianceResult.Compliant)
            .SingleOrDefaultAsync();
        if (bid == null) return null;

        var tender = await _db.Tenders.AsNoTracking().SingleAsync(t => t.Id == bid.TenderId);
        var criteria = await _db.EvaluationCriteria.AsNoTracking().Where(c => c.TenderId == bid.TenderId).OrderBy(c => c.Id).ToListAsync();
        var allScores = await _db.TechnicalEvaluations.AsNoTracking().Where(e => e.BidId == bidId).ToListAsync();
        var scores = allScores.Where(e => e.EvaluatorUserId == evaluatorId).ToList();
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
        foreach (var member in model.Members)
        {
            member.IsCurrentUser = member.UserId == userId;
            member.IsSelected = member.UserId == evaluatorId;
            var memberScores = new List<EvaluationScoreViewModel>();
            foreach (var criterion in model.Input.Scores)
            {
                var saved = allScores.FirstOrDefault(e => e.EvaluatorUserId == member.UserId && e.CriterionId == criterion.CriterionId);
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
            || model.Input.Scores.Count == 0 || criteria.Any(c => !c.IsLocked || c.MaxScore <= 0 || c.WeightPercent <= 0)
            || model.TechnicalWeight <= 0 || model.FinancialWeight <= 0
            || model.TechnicalWeight + model.FinancialWeight != 100
            || criteria.Any(c => c.Type != Statuses.CriterionType.Technical && c.Type != Statuses.CriterionType.Financial))
            model.ConfigurationMessage = "Saving requires four different committee members and locked technical/financial criteria with valid weights totaling 100%. Contact Sourcing.";

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
        var comparisonBids = await _db.Bids.AsNoTracking().Where(b => b.TenderId == bid.TenderId
            && b.SubmittedAt != null && (b.Status == Statuses.BidStatus.Submitted
                || b.Status == Statuses.BidStatus.UnderReview || b.Status == Statuses.BidStatus.Compliant
                || b.Status == Statuses.BidStatus.NonCompliant))
            .Select(b => new
            {
                b.Id, b.SupplierProfile.CompanyName, b.DeliveryDays, b.TotalPrice, b.Currency,
                Result = b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id).Select(c => c.Result).FirstOrDefault(),
                HasMatchingItems = b.BidItems.Count == b.Tender.Requisition.PurchaseRequisitionItems.Count
                    && b.BidItems.Count > 0
                    && b.BidItems.All(i => i.RequisitionItem.RequisitionId == b.Tender.RequisitionId
                        && i.Quantity == i.RequisitionItem.Quantity && i.UnitPrice >= 0
                        && i.TaxAmount >= 0 && i.ShippingCost >= 0 && (i.InstallationCost ?? 0) >= 0)
                    && b.BidItems.Select(i => i.RequisitionItemId).Distinct().Count() == b.BidItems.Count
                    && b.TotalPrice == b.BidItems.Sum(i => i.Quantity * i.UnitPrice + i.TaxAmount + i.ShippingCost + (i.InstallationCost ?? 0))
            }).ToListAsync();
        var eligible = comparisonBids.Where(b => b.Result == Statuses.ComplianceResult.Compliant).ToList();
        if (comparisonBids.Any(b => b.Result != Statuses.ComplianceResult.Compliant && b.Result != Statuses.ComplianceResult.NonCompliant))
            model.FinancialMessage = "Financial comparison is waiting for all submitted offers to complete compliance review.";
        else if (eligible.Count == 0 || eligible.Any(b => b.TotalPrice <= 0 || !b.HasMatchingItems || string.IsNullOrWhiteSpace(b.Currency))
            || eligible.Select(b => b.Currency.Trim().ToUpperInvariant()).Distinct().Count() != 1)
            model.FinancialMessage = "Financial comparison requires positive comparable supplier totals, matching requested quantities and one currency. Contact Sourcing.";
        else
        {
            model.IsLowestPrice = bid.TotalPrice == eligible.Min(b => b.TotalPrice);
            model.FinancialPoints = model.IsLowestPrice.Value ? model.FinancialWeight : 0;
        }
        var myTechnicalScores = await _db.TechnicalEvaluations.AsNoTracking()
            .Where(e => e.Bid.TenderId == bid.TenderId && e.EvaluatorUserId == userId).ToListAsync();
        var myFinancialScores = await _db.FinancialEvaluations.AsNoTracking()
            .Where(e => e.Bid.TenderId == bid.TenderId && e.EvaluatorUserId == userId).ToListAsync();
        foreach (var offer in eligible.OrderBy(b => b.CompanyName))
        {
            model.ComparisonOffers.Add(new EvaluationOfferViewModel
            {
                Id = offer.Id, CompanyName = offer.CompanyName, TotalPrice = offer.TotalPrice,
                Currency = offer.Currency, DeliveryDays = offer.DeliveryDays,
                IsLowestPrice = model.FinancialMessage == null ? offer.TotalPrice == eligible.Min(b => b.TotalPrice) : null,
                HasSavedEvaluation = model.ConfigurationMessage == null && model.FinancialMessage == null
                    && model.Input.Scores.All(criterion => myTechnicalScores.Any(e => e.BidId == offer.Id
                        && e.CriterionId == criterion.CriterionId && e.Score >= 0 && e.Score <= criterion.MaxScore))
                    && myFinancialScores.Any(e => e.BidId == offer.Id && e.TotalCost == offer.TotalPrice
                        && e.Score == (offer.TotalPrice == eligible.Min(b => b.TotalPrice) ? model.FinancialWeight : 0))
            });
        }
        return model;
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
        return decimal.Round(total, 2);
    }
}
