using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Areas.Evaluation.ViewComponents;

public class EvaluationResultsReportViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;
    private readonly AiBidAnalysisService _ai;

    public EvaluationResultsReportViewComponent(ApplicationDbContext db, AiBidAnalysisService ai)
    {
        _db = db;
        _ai = ai;
    }

    public async Task<IViewComponentResult> InvokeAsync(int tenderId)
    {
        var user = HttpContext.User;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId)
            || !user.IsInRole(Roles.SourcingOfficer)) return Content(string.Empty);

        var model = await new EvaluationReportReader(_db, _ai)
            .BuildAsync(tenderId, userId, true, Url, HttpContext.RequestAborted);
        if (model?.SubmittedAt == null) return Content(string.Empty);
        return View("~/Areas/Evaluation/Views/Report/_OfficerReport.cshtml", model);
    }
}
