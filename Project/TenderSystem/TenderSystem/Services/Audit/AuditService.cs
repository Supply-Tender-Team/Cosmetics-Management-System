using System.Security.Claims;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;

namespace TenderSystem.Services.Audit;

public class AuditService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    // Call after saving the business action. Never include passwords or sensitive data.
    public Task LogAsync(string action, string entityName, string entityId,
        string? details = null, string? oldValues = null)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return WriteAsync(userId, action, entityName, entityId, details, oldValues);
    }

    // Auth flows can identify the account before HttpContext.User is authenticated.
    public Task LogForUserAsync(string? userId, string action, string entityName,
        string entityId, string? details = null, string? oldValues = null)
    {
        return WriteAsync(userId, action, entityName, entityId, details, oldValues);
    }

    private async Task WriteAsync(string? userId, string action, string entityName,
        string entityId, string? details, string? oldValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (action.Length > 100 || entityName.Length > 100 || entityId.Length > 50)
            throw new ArgumentException("Audit values exceed the database field lengths.");

        var context = _httpContextAccessor.HttpContext;
        var log = new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = details,
            IpAddress = context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
