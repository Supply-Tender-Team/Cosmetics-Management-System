using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Services.Ai;

namespace TenderSystem.Controllers;

/// <summary>
/// The AI tender summary card (partial Views/Shared/_AiTenderSummary.cshtml). Any signed-in user may call it; the service decides who may
/// see a tender's summary (evaluation committee after Ready, Sourcing Manager, Top Committee) and answers "denied" to everybody else.
/// </summary>
[Authorize]
public class AiSummaryController : Controller
{
    private readonly AiTenderSummaryService _summary;

    public AiSummaryController(AiTenderSummaryService summary)
    {
        _summary = summary;
    }

    public class GenerateRequest { public int TenderId { get; set; } }

    [HttpGet]
    public async Task<IActionResult> Get(int tenderId, CancellationToken cancellationToken) =>
        Json(await _summary.GetAsync(tenderId, User, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request, CancellationToken cancellationToken) =>
        Json(await _summary.GenerateAsync(request?.TenderId ?? 0, User, cancellationToken));
}
