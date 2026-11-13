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
    public async Task LogAsync(string action, string entityName, string entityId,
        string? details = null, string? oldValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (action.Length > 100 || entityName.Length > 100 || entityId.Length > 50)
            throw new ArgumentException("Audit values exceed the database field lengths.");

        var context = _httpContextAccessor.HttpContext;
        var log = new AuditLog
        {
            UserId = context?.User.FindFirstValue(ClaimTypes.NameIdentifier),
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
