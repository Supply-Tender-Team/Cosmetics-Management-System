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

    // Read-only list with search (name, email, subject) and All / Unread / Read tabs. Newest first.
    public async Task<IActionResult> Index(string? q, string? filter)
    {
        var search = CleanSearch(q);
        var tab = CleanFilter(filter);

        var all = _db.ContactMessages.AsNoTracking();
        var matching = all;
        if (search != null)
            matching = matching.Where(m => m.FullName.Contains(search) || m.Email.Contains(search) || m.Subject.Contains(search));

        var model = new ContactMessagesPageViewModel
        {
            Search = search,
            Filter = tab,
            InboxIsEmpty = !await all.AnyAsync(),
            AllCount = await matching.CountAsync(),
            UnreadCount = await matching.CountAsync(m => !m.IsRead)
        };
        model.ReadCount = model.AllCount - model.UnreadCount;

        if (tab == "unread") matching = matching.Where(m => !m.IsRead);
        if (tab == "read") matching = matching.Where(m => m.IsRead);

        model.Messages = await matching
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
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

    // q and filter are only used to build the "Back to messages" link, so the Admin returns to the same view.
    public async Task<IActionResult> Details(int id, string? q, string? filter)
    {
        var message = await _db.ContactMessages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (message is null) return NotFound();

        ViewData["BackSearch"] = CleanSearch(q);
        ViewData["BackFilter"] = CleanFilter(filter);
        return View(message);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id, string? q, string? filter)
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
        return RedirectToAction(nameof(Details), new { id, q = CleanSearch(q), filter = CleanFilter(filter) });
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
}
