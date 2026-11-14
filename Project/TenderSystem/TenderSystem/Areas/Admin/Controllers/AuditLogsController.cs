using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class AuditLogsController : Controller
{
    // OWNER: Abdallah
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;

    public AuditLogsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search, string? from, string? to, int page = 1)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];

        var model = new AuditLogsPageModel { Search = search, From = from, To = to };
        DateTime? fromDate = null;
        DateTime? toDate = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                fromDate = parsed;
            else
                model.Error = "Enter a valid From date.";
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                toDate = parsed;
            else
                model.Error = "Enter a valid To date.";
        }
        if (fromDate > toDate && toDate.HasValue)
            model.Error = "From date must be on or before To date.";
        if (toDate == DateTime.MaxValue.Date)
            model.Error = "Choose an earlier To date.";
        if (model.Error is not null) return View(model);

        var query = _db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(log => log.Action.Contains(search) || log.EntityName.Contains(search) ||
                log.EntityId.Contains(search) || (log.UserId != null && log.UserId.Contains(search)) ||
                (log.User != null && log.User.FullName.Contains(search)));
        if (fromDate.HasValue) query = query.Where(log => log.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(log => log.CreatedAt < toDate.Value.AddDays(1));

        model.TotalCount = await query.CountAsync();
        model.PageCount = Math.Max(1, (int)Math.Ceiling(model.TotalCount / (double)PageSize));
        model.Page = Math.Min(Math.Max(page, 1), model.PageCount);
        model.Logs = await query.OrderByDescending(log => log.CreatedAt).ThenByDescending(log => log.Id)
            .Skip((model.Page - 1) * PageSize).Take(PageSize)
            .Select(log => new AuditLogRow
            {
                Actor = log.User != null ? log.User.FullName : (log.UserId ?? "Unknown"),
                Action = log.Action,
                EntityName = log.EntityName,
                EntityId = log.EntityId,
                CreatedAt = log.CreatedAt,
                HasHiddenDetails = log.NewValues != null || log.OldValues != null
            }).ToListAsync();
        return View(model);
    }
}

public class AuditLogsPageModel
{
    public string? Search { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Error { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<AuditLogRow> Logs { get; set; } = [];
}

public class AuditLogRow
{
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool HasHiddenDetails { get; set; }
}
