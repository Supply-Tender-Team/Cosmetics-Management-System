using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;

namespace TenderSystem.Services.Notifications;

public class NotificationService
{
    private readonly ApplicationDbContext _context;

    public NotificationService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task NotifyAsync(
        string userId,
        string type,
        string title,
        string message,
        string? link = null)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(type) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "User ID, type, title and message are required.");
        }

        userId = userId.Trim();

        bool userExists = await _context.Users.AnyAsync(user => user.Id == userId);
        if (!userExists)
        {
            throw new ArgumentException("The user does not exist.", nameof(userId));
        }

        var notification = CreateNotification(userId, type, title, message, link);
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    public async Task NotifyRoleAsync(
        string role,
        string type,
        string title,
        string message,
        string? link = null)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role is required.", nameof(role));
        }

        var userIds = await _context.UserRoles
            .Join(
                _context.Roles,
                userRole => userRole.RoleId,
                roleRecord => roleRecord.Id,
                (userRole, roleRecord) => new { userRole.UserId, roleRecord.Name })
            .Where(item => item.Name == role.Trim())
            .Select(item => item.UserId)
            .Distinct()
            .ToListAsync();

        foreach (string userId in userIds)
        {
            await NotifyAsync(userId, type, title, message, link);
        }
    }

    private static Notification CreateNotification(
        string userId,
        string type,
        string title,
        string message,
        string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            link = null;
        }
        else
        {
            link = link.Trim();

            if (!link.StartsWith('/') ||
                link.StartsWith("//") ||
                link.Contains('\\') ||
                link.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "The notification link must be a local website path.",
                    nameof(link));
            }
        }

        var notification = new Notification
        {
            UserId = userId,
            Type = type.Trim(),
            Title = title.Trim(),
            Message = message.Trim(),
            Link = link,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        Validator.ValidateObject(
            notification,
            new ValidationContext(notification),
            validateAllProperties: true);

        return notification;
    }
}