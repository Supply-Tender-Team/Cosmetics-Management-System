using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Data;
using TenderSystem.Services.Audit;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class NotificationsController : Controller
{
    private const string TestNotificationType = "Test";
    private readonly ApplicationDbContext _context;

    private readonly AuditService _audit;

    public NotificationsController(ApplicationDbContext context, AuditService audit)
    {
        _context = context;
        _audit = audit;
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

        // Older offer notifications point to /Sourcing/Offers, but the
        // Sourcing Offers controller exposes its comparison page as Compare.
        foreach (var notification in notifications)
        {
            notification.Link = ResolveNotificationLink(notification.Type, notification.Link);
        }

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
                item.Id,
                item.Type,
                item.Title,
                item.Message,
                item.Link,
                item.IsRead,
                item.CreatedAt
            })
            .ToListAsync();

        return Json(new
        {
            items = notifications.Select(item =>
            {
                var link = ResolveNotificationLink(item.Type, item.Link);
                return new
                {
                    item.Id,
                    item.Title,
                    item.Message,
                    item.IsRead,
                    createdAt = item.CreatedAt.ToString("dd MMM yyyy, HH:mm") + " UTC",
                    link = !string.IsNullOrWhiteSpace(link) && Url.IsLocalUrl(link) ? link : null
                };
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        bool isAjaxRequest = string.Equals(
            Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.UserId == userId &&
                item.Type != TestNotificationType);

        if (notification is null)
        {
            return isAjaxRequest ? NotFound() : RedirectToAction(nameof(Index));
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
            await _audit.LogAsync("MarkedRead", "Notification", notification.Id.ToString(), "Notification marked as read.");

            TempData["ToastTitle"] = "Notification updated";
            TempData["ToastMessage"] = "Notification marked as read.";
            TempData["ToastType"] = "success";
        }

        return isAjaxRequest ? Ok(new { success = true }) : RedirectToAction(nameof(Index));
    }

    private static string? ResolveNotificationLink(string type, string? link)
    {
        if (string.Equals(type, "OfferReceived", StringComparison.OrdinalIgnoreCase)
            && string.Equals(link?.TrimEnd('/'), "/Sourcing/Offers", StringComparison.OrdinalIgnoreCase))
        {
            return "/Sourcing/Compliance";
        }

        return link;
    }
}
