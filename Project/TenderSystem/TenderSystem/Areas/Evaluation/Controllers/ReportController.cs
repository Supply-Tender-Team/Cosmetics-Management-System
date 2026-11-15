using TenderSystem.Areas.Evaluation.Services;
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
public class ReportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AiBidAnalysisService _ai;

    public ReportController(ApplicationDbContext db, AiBidAnalysisService ai)
    {
        _db = db;
        _ai = ai;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var model = await new EvaluationReportReader(_db, _ai).BuildAsync(id, userId, User.IsInRole(Roles.SourcingOfficer), Url, ct);
        return model == null ? NotFound() : View(model);
    }

}
