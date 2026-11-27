using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OfferAssistantController(ApplicationDbContext db, AiOfferAdvisorService advisor, AuditService audit, AiSavedResultStore saved, ILogger<OfferAssistantController> logger) : Controller
{
    // The last analysis of this user and tender is saved (AI-24): a refresh or a later visit shows it again without calling the model.
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    [HttpGet]
    public async Task<IActionResult> Current(int id, CancellationToken ct)
    {
        var request = await BuildRequestAsync(id, ct);
        if (request == null) return NotFound();
        var row = await saved.GetAsync(AiSavedResultStore.Advisor, UserId, id.ToString(CultureInfo.InvariantCulture), ct);
        var previous = row is null ? null : JsonSerializer.Deserialize<OfferAssistantResultViewModel>(row.ResultJson);
        if (previous == null) return NoContent();
        ViewData["AdviceStale"] = previous.Fingerprint != CurrentFingerprint(request);
        return PartialView("~/Areas/Evaluation/Views/Assessments/_OfferAssistantResult.cshtml", previous);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(int id, CancellationToken ct)
    {
        var request = await BuildRequestAsync(id, ct);
        if (request == null) return NotFound();
        if (request.Offers.Count > AiOfferAdvisorService.MaxOffers)
            return BadRequest(new { message = "Too many eligible offers for the advisor. No offers were omitted." });
        await audit.LogAsync("OfferAdviceRequested", "Tender", id.ToString(CultureInfo.InvariantCulture),
            "Eligible offers: " + request.Offers.Count + "; criteria: " + request.Criteria.Count);
        var result = await advisor.AnalyzeAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (ct.IsCancellationRequested) return StatusCode(499);
        if (!result.Ok || result.Advice == null) {
            if (result.Status == "invalid-request") logger.LogWarning("Offer advisor input rejected for tender {TenderId}", id);
            return BadRequest(new { status = result.Status, message = result.Message });
        }
        // Recheck access and facts after the potentially long model request.
        var current = await BuildRequestAsync(id, ct);
        if (current == null) return NotFound();
        var advice = result.Advice;
        var model = new OfferAssistantResultViewModel {
            TenderId = id, TenderNumber = request.TenderNumber, SuggestedBidId = advice.SuggestedBidId,
            SuggestedSupplier = request.Offers.FirstOrDefault(o => o.BidId == advice.SuggestedBidId)?.Supplier ?? "",
            GeneratedAtUtc = advice.GeneratedAtUtc, Status = result.Status, Message = result.Message,
            Model = result.Model, Fingerprint = advice.Fingerprint, Reasons = advice.Reasons.ToList(),
            Checks = advice.Checks.ToList(), CoverageNotes = advice.CoverageNotes.ToList(),
            Alternatives = advice.Alternatives.Select(a => request.Offers.Single(o => o.BidId == a.BidId).Supplier + ": " + a.Comparison).ToList(),
            ScopeNote = "All " + request.Offers.Count + " eligible submitted offers; dynamic tender criteria."
        };
        await saved.SaveAsync(AiSavedResultStore.Advisor, UserId, id.ToString(CultureInfo.InvariantCulture), "", advice.Fingerprint,
            JsonSerializer.Serialize(model), result.Model ?? "", ct);
        ViewData["AdviceStale"] = advice.Fingerprint != CurrentFingerprint(current);
        return PartialView("~/Areas/Evaluation/Views/Assessments/_OfferAssistantResult.cshtml", model);
    }

    // The provider fingerprint helper assumes at least one offer.
    private static string CurrentFingerprint(AiOfferAdvisorRequest request) =>
        request.Offers.Count == 0 || request.Offers.Count > AiOfferAdvisorService.MaxOffers ? "" : AiOfferAdvisorService.FingerprintOf(request);

    private async Task<AiOfferAdvisorRequest?> BuildRequestAsync(int id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return null;
        var tender = await db.Tenders.AsNoTracking().Include(t => t.Requisition).ThenInclude(r => r.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(t => t.Id == id && t.PublishDate != null
                && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                    || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation)
                && t.CommitteeMembers.Any(m => m.UserId == userId), ct);
        if (tender == null) return null;
        var releaseLink = Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id });
        if (!await db.Notifications.AsNoTracking().AnyAsync(n => n.UserId == userId && n.Type == "TenderEvaluation" && n.Link == releaseLink, ct)) return null;
        var criteria = await db.EvaluationCriteria.AsNoTracking().Where(c => c.TenderId == id).OrderBy(c => c.Id).ToListAsync(ct);
        var bids = await db.Bids.AsNoTracking().Where(b => b.TenderId == id && b.SubmittedAt != null
            && (b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted))
            .Include(b => b.SupplierProfile).Include(b => b.BidItems).ThenInclude(i => i.RequisitionItem)
            .Include(b => b.BidDocuments).AsSplitQuery().OrderBy(b => b.Id).ToListAsync(ct);
        var limits = new List<string> {
            "Uploaded file contents, pictures, scans, samples and guarantee verification were not read. File names are not proof of compliance.",
            "Committee scores, private comments and votes are not used. Advice does not calculate scores or decide the official recommendation."
        };
        string Cut(string? text, int length) {
            text ??= "";
            if (text.Length <= length) return text;
            if (!limits.Contains("Some long source text was shortened for the advisor.")) limits.Add("Some long source text was shortened for the advisor.");
            return text[..length];
        }
        var requirements = Cut(tender.Description + "\nTerms: " + tender.Terms + "\nRequested items: " +
            string.Join("\n", tender.Requisition.PurchaseRequisitionItems.OrderBy(i => i.Id).Select(i => i.ItemName + ": " + i.Quantity.ToString(CultureInfo.InvariantCulture) + " " + i.Unit + "; " + i.Specifications)), 3500);
        var offers = bids.Select(b => new AiOfferInput(b.Id, b.SupplierProfile.CompanyName, b.TotalPrice, b.Currency,
            b.DeliveryDays, b.ValidityDays, Cut(b.PaymentTerms,200), Cut(b.TechnicalSummary,600),
            new List<string> { Cut(string.Join("; ", b.BidItems.OrderBy(i => i.Id).Select(i => Cut(i.RequisitionItem.ItemName + "; offered quantity " + i.Quantity.ToString(CultureInfo.InvariantCulture) + "; specifications: " + i.OfferedSpecifications, 220))), 600) },
            new List<string> { Cut(string.Join("; ", b.BidDocuments.OrderBy(d => d.Id).Select(d => d.DocType + ": " + d.FileName)), 400) },
            "Recorded eligible status: " + b.Status)).ToList();
        return new(id, tender.TenderNumber, tender.Title, requirements,
            criteria.Select(c => new AiOfferCriterion(c.Id,c.Type,c.Name,c.WeightPercent,c.MaxScore)).ToList(), offers, limits);
    }
}
