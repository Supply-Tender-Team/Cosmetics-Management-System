using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class ContactMessagesController : Controller
{
    private const int PreviewLength = 140;
    private const int MaxSearchLength = 100;

    private readonly ApplicationDbContext _db;

    private readonly AuditService _audit;

    public ContactMessagesController(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // Read-only list with search (name, email, subject), All / Unread / Read tabs, newest / oldest order and paging.
    public async Task<IActionResult> Index(string? q, string? filter, string? sort, int page = 1)
    {
        var search = CleanSearch(q);
        var tab = CleanFilter(filter);
        var order = CleanSort(sort);

        var all = _db.ContactMessages.AsNoTracking();
        var matching = all;
        if (search != null)
            matching = matching.Where(m => m.FullName.Contains(search) || m.Email.Contains(search) || m.Subject.Contains(search));

        // One grouped query per scope (whole inbox, and the search result when a search is active).
        var totals = await CountByReadAsync(all);
        var found = search == null ? totals : await CountByReadAsync(matching);

        var model = new ContactMessagesPageViewModel
        {
            Search = search,
            Filter = tab,
            Sort = order,
            TotalMessages = totals.Unread + totals.Read,
            TotalUnread = totals.Unread,
            AllCount = found.Unread + found.Read,
            UnreadCount = found.Unread,
            ReadCount = found.Read
        };
        model.ResultCount = tab == "unread" ? model.UnreadCount : tab == "read" ? model.ReadCount : model.AllCount;
        model.PageCount = Math.Max(1, (int)Math.Ceiling(model.ResultCount / (double)ContactMessagesPageViewModel.PageSize));
        model.Page = Math.Clamp(page, 1, model.PageCount);

        if (tab == "unread") matching = matching.Where(m => !m.IsRead);
        if (tab == "read") matching = matching.Where(m => m.IsRead);

        // The Id is the tie-breaker so the order (and therefore the pages) is stable.
        var ordered = order == "oldest"
            ? matching.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            : matching.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id);

        model.Messages = await ordered
            .Skip((model.Page - 1) * ContactMessagesPageViewModel.PageSize)
            .Take(ContactMessagesPageViewModel.PageSize)
            .Select(m => new ContactMessageRow
            {
                Id = m.Id,
                FullName = m.FullName,
                Email = m.Email,
                Subject = m.Subject,
                Preview = m.Message.Length > PreviewLength ? m.Message.Substring(0, PreviewLength) : m.Message,
                CreatedAt = m.CreatedAt,
                IsRead = m.IsRead
            }).ToListAsync();

        return View(model);
    }

    // q, filter, sort and page are only used to build the "Back to messages" link, so the Admin returns to the same view.
    public async Task<IActionResult> Details(int id, string? q, string? filter, string? sort, int page = 1)
    {
        var message = await _db.ContactMessages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (message is null) return NotFound();

        ViewData["BackSearch"] = CleanSearch(q);
        ViewData["BackFilter"] = CleanFilter(filter);
        ViewData["BackSort"] = CleanSort(sort);
        ViewData["BackPage"] = CleanPage(page);
        return View(message);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id, string? q, string? filter, string? sort, int page = 1)
    {
        var message = await _db.ContactMessages.FindAsync(id);
        if (message is null) return NotFound();
        if (!message.IsRead)
        {
            message.IsRead = true;
            try
            {
                await _db.SaveChangesAsync();
                await _audit.LogAsync("MarkedRead", "ContactMessage", message.Id.ToString());
                TempData["ContactReadSuccess"] = "Message marked as read.";
            }
            catch (DbUpdateException)
            {
                TempData["ContactReadError"] = "Could not mark the message as read. Please try again.";
            }
        }
        return RedirectToAction(nameof(Details), new
        {
            id,
            q = CleanSearch(q),
            filter = CleanFilter(filter),
            sort = CleanSort(sort),
            page = CleanPage(page)
        });
    }

    private static async Task<(int Unread, int Read)> CountByReadAsync(IQueryable<Data.Entities.Contact.ContactMessage> query)
    {
        var groups = await query.GroupBy(m => m.IsRead)
            .Select(g => new { IsRead = g.Key, Count = g.Count() }).ToListAsync();
        return (groups.Where(g => !g.IsRead).Sum(g => g.Count), groups.Where(g => g.IsRead).Sum(g => g.Count));
    }

    private static string? CleanSearch(string? q)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q)) return null;
        return q.Length > MaxSearchLength ? q[..MaxSearchLength] : q;
    }

    private static string CleanFilter(string? filter)
    {
        filter = filter?.Trim().ToLowerInvariant();
        return filter != null && ContactMessagesPageViewModel.Filters.Contains(filter) ? filter : "all";
    }

    private static string CleanSort(string? sort)
    {
        sort = sort?.Trim().ToLowerInvariant();
        return sort != null && ContactMessagesPageViewModel.Sorts.Contains(sort) ? sort : "newest";
    }

    private static int CleanPage(int page) => Math.Clamp(page, 1, 100_000);
}
