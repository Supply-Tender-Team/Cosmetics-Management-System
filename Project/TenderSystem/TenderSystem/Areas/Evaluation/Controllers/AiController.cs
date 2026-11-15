using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Models;
using TenderSystem.Data;
using TenderSystem.Services.Ai;

namespace TenderSystem.Areas.Evaluation.Controllers;

[Area("Evaluation")]
[Authorize(Roles = Roles.EvaluationCommittee + "," + Roles.Requester + "," + Roles.SourcingManager + "," + Roles.SourcingOfficer)]
public class AiController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AiBidAnalysisService _ai;

    public AiController(ApplicationDbContext db, AiBidAnalysisService ai)
    {
        _db = db;
        _ai = ai;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var model = await BuildPageAsync(id, userId, ct);
        return model == null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(int id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var model = await BuildPageAsync(id, userId, ct);
        if (model == null) return NotFound();
        var run = await _ai.AnalyzeAsync(id, userId, ct);
        model.Message = run.Message;
        model.Items = await _ai.GetLatestAsync(id, userId, ct);
        return View("Index", model);
    }

    private async Task<AiAnalysisViewModel?> BuildPageAsync(int? id, string userId, CancellationToken ct)
    {
        var tenders = await _db.Tenders.AsNoTracking()
            .Where(t => t.PublishDate != null && t.CommitteeMembers.Any(m => m.UserId == userId)
                && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                    || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new EvaluationTenderViewModel { Id = t.Id, TenderNumber = t.TenderNumber, Title = t.Title })
            .ToListAsync(ct);
        var links = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Type == "TenderEvaluation").Select(n => n.Link).ToListAsync(ct);
        tenders = tenders.Where(t => links.Contains(Url.Action("Index", "CommitteeBrief",
            new { area = "Sourcing", id = t.Id }))).ToList();
        var model = new AiAnalysisViewModel { Tenders = tenders, Enabled = _ai.Enabled, TenderId = id };
        if (!id.HasValue) return model;
        var selected = tenders.FirstOrDefault(t => t.Id == id.Value);
        if (selected == null) return null;
        model.TenderNumber = selected.TenderNumber;
        model.Items = await _ai.GetLatestAsync(id.Value, userId, ct);
        model.BidStatuses = await _db.Bids.AsNoTracking().Where(b => b.TenderId == id.Value)
            .ToDictionaryAsync(b => b.Id, b => b.Status, ct);
        return model;
    }
}
