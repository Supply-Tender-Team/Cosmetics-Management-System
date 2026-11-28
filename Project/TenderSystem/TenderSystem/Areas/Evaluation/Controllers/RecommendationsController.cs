using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class RecommendationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly CommitteeRecommendationService _recommendations;
    private readonly EvaluationReportService _reports;
    private readonly AuditService _audit;

    public RecommendationsController(ApplicationDbContext db, CommitteeRecommendationService recommendations,
        EvaluationReportService reports, AuditService audit)
    { _db = db; _recommendations = recommendations; _reports = reports; _audit = audit; }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    // Polling endpoint: return a stage key so waiting pages can detect readiness/vote changes.
    public async Task<IActionResult> Status(int tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        // Read authorized progress from the service; do not trust counters posted by the browser.
        var state = await _recommendations.ReadAsync(tenderId, userId, SharedLink(tenderId));
        return state == null ? NotFound() : Json(new { stageKey = state.StageKey });
    }

    [HttpPost, ValidateAntiForgeryToken]
    // Ready POST: recheck all eligible-offer saves before locking this member for the entire tender.
    public async Task<IActionResult> Ready(int tenderId, bool confirmReady)
    {
        if (!ModelState.IsValid || !confirmReady) return BadRequest("Confirm Ready to lock all your saved evaluations.");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        if (!await _db.CommitteeMembers.AnyAsync(m => m.TenderId == tenderId && m.UserId == userId)) return NotFound();
        // Serialize state-changing requests on this tender; re-read state after taking the lock.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockTenderAsync(tenderId);
        // Read authorized progress from the service; do not trust counters posted by the browser.
        var state = await _recommendations.ReadAsync(tenderId, userId, SharedLink(tenderId));
        if (state == null) return NotFound();
        if (state.IsReady) return RedirectToOffers(tenderId);
        if (!state.CanReady) return Conflict("Save changes for every eligible offer before confirming Ready.");
        var now = DateTime.UtcNow;
        // One Ready confirmation locks this member's evaluations for the whole tender.
        _db.EvaluationCommitteeSubmissions.Add(new EvaluationCommitteeSubmission
        { TenderId = tenderId, EvaluatorUserId = userId, ReadyAt = now, VotingRound = state.VotingRound });
        var bidIds = state.Offers.Select(o => o.BidId).ToList();
        var forms = await _db.FinancialEvaluations.Where(e => e.EvaluatorUserId == userId && bidIds.Contains(e.BidId)).ToListAsync();
        // Keep existing report readers and Officer tracking compatible with the single tender confirmation.
        foreach (var form in forms) form.ReadyAt = now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("EvaluationReady", "Tender", tenderId.ToString(), "Member locked all saved eligible-offer evaluations. No report sent yet.");
        // Commit this member's Ready confirmation, compatibility flags and Audit entry.
        await transaction.CommitAsync();
        TempData["EvaluationSaved"] = "Your evaluations are Ready and locked. Recommendation voting opens when all four members are Ready.";
        return RedirectToOffers(tenderId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    // Vote POST: one confirmed eligible bid per member and current round, after all four are Ready.
    public async Task<IActionResult> Vote(int tenderId, int recommendedBidId, int votingRound)
    {
        if (!ModelState.IsValid) return BadRequest("Choose an eligible offer and reload the voting round.");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        if (!await _db.CommitteeMembers.AnyAsync(m => m.TenderId == tenderId && m.UserId == userId)) return NotFound();
        // Serialize state-changing requests on this tender; re-read state after taking the lock.
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockTenderAsync(tenderId);
        // Read authorized progress from the service; do not trust counters posted by the browser.
        var state = await _recommendations.ReadAsync(tenderId, userId, SharedLink(tenderId));
        if (state == null) return NotFound();
        if (state.IsSubmitted) return RedirectToOffers(tenderId);
        // Require four Ready members and valid committee totals before accepting a vote.
        if (!state.AllReady || state.ConfigurationMessage != null || state.Offers.Any(o => !o.CommitteeTotal.HasValue))
            return Conflict("All four members must have valid Ready evaluations before voting.");
        // Reject stale pages from an earlier round and choices outside this tender's eligible bids.
        if (state.VotingRound != votingRound) return Conflict("A new voting round has started. Reload this page before choosing again.");
        if (!state.Offers.Any(o => o.BidId == recommendedBidId)) return BadRequest("Choose an eligible offer from this tender.");
        // Load current assigned members' Ready/vote rows; repeated identical votes do not add another vote.
        var votes = await _db.EvaluationCommitteeSubmissions.Where(s => s.TenderId == tenderId
            && _db.CommitteeMembers.Any(m => m.TenderId == tenderId && m.UserId == s.EvaluatorUserId)).ToListAsync();
        var mine = votes.Single(s => s.EvaluatorUserId == userId);
        if (mine.VotedAt != null)
            return mine.RecommendedBidId == recommendedBidId ? RedirectToOffers(tenderId) : Conflict("Your choice is confirmed for this round.");
        // Store the choice on the existing member-tender confirmation row and audit it.
        mine.RecommendedBidId = recommendedBidId;
        mine.VotedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("RecommendationVoted", "Tender", tenderId.ToString(), $"round={votingRound}; bid={recommendedBidId}");
        // Only the fourth confirmed vote triggers winner resolution; earlier votes wait for the others.
        if (votes.All(v => v.VotedAt != null))
        {
            var winner = CommitteeRecommendationService.ResolveWinner(votes, state.Offers);
            if (!winner.HasValue)
            {
                var counts = string.Join(", ", votes.GroupBy(v => v.RecommendedBidId).Select(g => $"bid={g.Key}: votes={g.Count()}"));
                await _audit.LogAsync("RecommendationTied", "Tender", tenderId.ToString(), $"round={votingRound}; {counts}; tied highest committee totals; new round required");
                // Reset only the choices; Ready confirmations and saved scores stay locked.
                foreach (var vote in votes) { vote.VotingRound++; vote.RecommendedBidId = null; vote.VotedAt = null; }
                await _db.SaveChangesAsync();
                TempData["EvaluationSaved"] = "The leading offers have equal votes and equal final committee scores. Discuss the recommendation and vote again. Your evaluations remain locked.";
            }
            else
            {
                // A resolved recommendation calls EvaluationReportService inside this same transaction.
                var reportLink = Url.Action("Index", "Evaluation", new { area = "Sourcing", tenderId })
                    ?? throw new InvalidOperationException("Could not generate Officer tracking link.");
                if (!await _reports.TryCreateAsync(tenderId, userId, reportLink, SharedLink(tenderId)))
                    return Conflict("The report could not be finalized. Check the saved evaluations and current committee assignments.");
                TempData["EvaluationSaved"] = "All four choices are confirmed. The report and recommended offer were sent to the Sourcing Officer.";
            }
        }
        else TempData["EvaluationSaved"] = "Your recommendation is confirmed. Waiting for the other members' choices.";
        // Commit the saved vote and any resolved report or new-round changes, then return to Offers.
        await transaction.CommitAsync();
        return RedirectToOffers(tenderId);
    }

    // Helpers: build the brief URL, lock the tender row, and redirect back to Offers.
    private string SharedLink(int tenderId) => Url.Action("Index", "CommitteeBrief", new { area = "Sourcing", id = tenderId })
        ?? throw new InvalidOperationException("Could not generate committee brief link.");
    private Task<int> LockTenderAsync(int tenderId) => _db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT [Id] FROM [Tenders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {tenderId}");
    private IActionResult RedirectToOffers(int tenderId) => RedirectToAction("Offers", "Evaluation", new { id = tenderId });
}
