using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Knowledge;
using TenderSystem.Services.Web;

namespace TenderSystem.Controllers;

/// <summary>
/// AI-18: the company documents the full-page assistant answers from. Admin only: the Admin adds documents and chooses which roles
/// may get answers from each one.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class KnowledgeController : Controller
{
    private readonly KnowledgeBaseService _knowledge;
    private readonly UserManager<ApplicationUser> _users;
    private readonly WebDomainService _webDomains;
    private readonly WebOptions _web;

    public KnowledgeController(KnowledgeBaseService knowledge, UserManager<ApplicationUser> users, WebDomainService webDomains, IOptions<WebOptions> web)
    {
        _knowledge = knowledge;
        _users = users;
        _webDomains = webDomains;
        _web = web.Value;
    }

    public class IndexModel
    {
        /// <summary>Web mode (AI-19): turned on in .env and a search key is set.</summary>
        public bool WebConfigured { get; set; }
        public bool WebEnabled { get; set; }
        public List<WebDomainItem> WebDomains { get; set; } = [];
        public bool Enabled { get; set; }
        public string Model { get; set; } = "";
        public int MaxMb { get; set; }
        public double MinScore { get; set; }
        public List<KnowledgeDocItem> Documents { get; set; } = [];
    }

    public class SearchRequest
    {
        public string? Question { get; set; }
        public string? Role { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new IndexModel
    {
        Enabled = _knowledge.Enabled,
        Model = _knowledge.Model,
        MaxMb = _knowledge.MaxMb,
        MinScore = _knowledge.MinScore,
        Documents = await _knowledge.ListAsync(ct),
        WebEnabled = _web.Enabled,
        WebConfigured = _web.Configured,
        WebDomains = await _webDomains.ListAsync(ct),
    });

    /// <summary>AI-19: allow a website for web mode (the domain and its subdomains).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDomain(string? domain, CancellationToken ct)
    {
        var (ok, message) = await _webDomains.AddAsync(_users.GetUserId(User) ?? "", domain, ct);
        return Done(ok, message, message);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDomain(int id, CancellationToken ct) =>
        Done(await _webDomains.RemoveAsync(id, ct), "The website was removed. Web mode no longer uses it.", "The website was not found.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 60_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file, string? title, string[]? roles, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Done(false, "Choose a file to add.");
        if (file.Length > _knowledge.MaxMb * 1024L * 1024L) return Done(false, $"The file is larger than {_knowledge.MaxMb} MB.");

        byte[] bytes;
        using (var memory = new MemoryStream())
        {
            await file.CopyToAsync(memory, ct);
            bytes = memory.ToArray();
        }

        var result = await _knowledge.AddAsync(_users.GetUserId(User) ?? "", bytes, file.FileName, title, roles ?? [], ct);
        return Done(result.Ok, result.Message, result.Message);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, bool enabled, CancellationToken ct) =>
        Done(await _knowledge.SetEnabledAsync(id, enabled, ct), enabled ? "The document is used for answers again." : "The document is switched off. It is kept but not used for answers.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRoles(int id, string[]? roles, CancellationToken ct) =>
        Done(await _knowledge.SetRolesAsync(id, roles ?? [], ct), "The roles of the document were saved.", "Choose at least one role.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Done(await _knowledge.DeleteAsync(id, ct), "The document was deleted.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reindex(int id, CancellationToken ct)
    {
        var result = await _knowledge.ReindexAsync(id, ct);
        return Done(result.Ok, result.Message, result.Message);
    }

    /// <summary>Try a question like a user of the chosen role would, and see which passages (and scores) the assistant would get.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000)]
    public async Task<IActionResult> Search([FromBody] SearchRequest? request, CancellationToken ct)
    {
        var question = request?.Question?.Trim() ?? "";
        var role = request?.Role ?? "";
        if (question.Length == 0 || question.Length > 1000 || !KnowledgeBaseService.AllowedRoleNames.Contains(role))
            return BadRequest(new { message = "Type a question and choose a role." });

        try
        {
            var hits = await _knowledge.SearchAsync(role, question, ct);
            return Json(new
            {
                minScore = _knowledge.MinScore,
                hits = hits.Select(h => new { title = h.Title, position = h.Position, score = h.Score, text = h.Text.Length > 600 ? h.Text[..600] + "…" : h.Text }),
            });
        }
        catch (Services.Ai.AiUnavailableException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>Goes back to the list with a message at the top.</summary>
    private IActionResult Done(bool ok, string message, string failure = "The document was not found.")
    {
        TempData["KbOk"] = ok;
        TempData["KbMessage"] = ok ? message : failure;
        return RedirectToAction(nameof(Index));
    }
}
