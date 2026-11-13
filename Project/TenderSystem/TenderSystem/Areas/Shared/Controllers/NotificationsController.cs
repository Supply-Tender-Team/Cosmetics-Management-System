using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Data;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class NotificationsController : Controller
{
    private const string TestNotificationType = "Test";
    private readonly ApplicationDbContext _context;

    public NotificationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Type != TestNotificationType)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync();

        return View(notifications);
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        int count = await _context.Notifications
            .AsNoTracking()
            .CountAsync(item =>
                item.UserId == userId &&
                item.Type != TestNotificationType &&
                !item.IsRead);

        return Json(new { count });
    }

    [HttpGet]
    public async Task<IActionResult> Latest()
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Type != TestNotificationType)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(5)
            .Select(item => new
            {
                item.Title,
                item.Message,
                item.Link,
                item.IsRead,
                item.CreatedAt
            })
            .ToListAsync();

        return Json(new
        {
            items = notifications.Select(item => new
            {
                item.Title,
                item.Message,
                item.IsRead,
                createdAt = item.CreatedAt.ToString("dd MMM yyyy, HH:mm") + " UTC",
                link = !string.IsNullOrWhiteSpace(item.Link) && Url.IsLocalUrl(item.Link)
                    ? item.Link
                    : null
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.UserId == userId &&
                item.Type != TestNotificationType);

        if (notification is not null && !notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();

            TempData["ToastTitle"] = "Notification updated";
            TempData["ToastMessage"] = "Notification marked as read.";
            TempData["ToastType"] = "success";
        }

        return RedirectToAction(nameof(Index));
    }
}