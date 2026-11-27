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

    // Use the signed-in account as the actor. Never pass passwords or sensitive data.
    public Task LogAsync(string action, string entityName, string entityId,
        string? details = null, string? oldValues = null)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return SaveAuditAsync(userId, action, entityName, entityId, details, oldValues);
    }

    // Auth flows can identify the account before HttpContext.User is authenticated.
    public Task LogForUserAsync(string? userId, string action, string entityName,
        string entityId, string? details = null, string? oldValues = null)
        => SaveAuditAsync(userId, action, entityName, entityId, details, oldValues);

    private async Task SaveAuditAsync(string? userId, string action, string entityName,
        string entityId, string? details, string? oldValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        // Match the entity's column limits before attempting a database save.
        if (action.Length > 100 || entityName.Length > 100 || entityId.Length > 50)
            throw new ArgumentException("Audit values exceed the database field lengths.");

        var httpContext = _httpContextAccessor.HttpContext;
        var auditLog = new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = details, // The caller's description is stored in the existing NewValues column.
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync();
    }
}
