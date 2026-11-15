using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Areas.Committee.Services;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Controllers;

[Area("Committee")]
[Authorize(Roles = Roles.TopCommittee)]
public class VotesController : Controller
{
    // OWNER: Abdallah (helping Dana with TOP-02) - votes, final decision retry and decision report.
    // URLs: /Committee/Votes/Cast/{tenderId} and /Committee/Votes/Result/{tenderId}.
    // Dana's pages can link here; none of her files are changed.
    private readonly CommitteeVotingService _voting;

    public VotesController(CommitteeVotingService voting) => _voting = voting;

    // /Committee/Votes - the voting list: tenders awaiting the committee and recorded decisions.
    // Current eligible members only (re-checked in the database, not just the login cookie).
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var list = await _voting.GetVotingListAsync(userId);
        if (list is null) return Forbid();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Cast(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var status = await _voting.GetStatusAsync(id, userId);
        if (status is null) return NotFound();

        return View(new CastVotePageViewModel { Status = status, Form = new CastVoteViewModel { TenderId = id } });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cast(CastVoteViewModel form)
    {
        // The member always comes from the sign-in, never from the form.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        if (!ModelState.IsValid)
        {
            var status = await _voting.GetStatusAsync(form.TenderId, userId);
            if (status is null) return NotFound();
            return View(new CastVotePageViewModel { Status = status, Form = form });
        }

        var result = await _voting.CastVoteAsync(form.TenderId, userId, form.Vote, form.Reason);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Message);
            var status = await _voting.GetStatusAsync(form.TenderId, userId);
            if (status is null) return NotFound();
            return View(new CastVotePageViewModel { Status = status, Form = form });
        }

        TempData["VoteSuccess"] = result.Message;
        return RedirectToAction(nameof(Cast), new { id = form.TenderId });
    }

    // Retry recording the final decision after all four votes (for example after the supplier was
    // re-approved or a clarification was resolved). Only the tender id comes from the form.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryFinalize(int tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var result = await _voting.RetryFinalizeAsync(tenderId, userId);
        TempData[result.Success ? "VoteSuccess" : "VoteError"] = result.Message;
        return RedirectToAction(nameof(Cast), new { id = tenderId });
    }

    // Re-send decision notifications that were not delivered. Current members only; recipients who
    // already have the notification are skipped, so this never creates duplicates.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendNotices(int tenderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var result = await _voting.ResendDecisionNoticesAsync(tenderId, userId);
        TempData[result.Success ? "ReportSuccess" : "ReportError"] = result.Message;
        return RedirectToAction(nameof(Result), new { id = tenderId });
    }

    // The final decision report: CURRENT committee members only, after the decision is recorded.
    [HttpGet]
    public async Task<IActionResult> Result(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var report = await _voting.GetDecisionReportAsync(id, userId);
        if (report is null) return NotFound();
        if (!report.CanView) return Forbid();
        return View(report);
    }
}
