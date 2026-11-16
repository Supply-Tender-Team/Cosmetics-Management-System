using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;

namespace TenderSystem.Services.Files;

public static class DocumentAccessLogService
{
    public static async Task<IReadOnlyList<DocumentWatermarkEntry>> RecordAndGetHistoryAsync(
        ApplicationDbContext db, string userId, string documentKind, int documentId,
        string action, DateTime accessedAtUtc, CancellationToken cancellationToken = default)
    {
        db.DocumentAccessLogs.Add(new DocumentAccessLog
        {
            UserId = userId,
            DocumentKind = documentKind,
            DocumentId = documentId,
            Action = action,
            CreatedAt = accessedAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);

        var rows = await db.DocumentAccessLogs.AsNoTracking()
            .Where(log => log.DocumentKind == documentKind && log.DocumentId == documentId)
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Select(log => new { log.UserId, log.User.FullName, log.User.Email, log.CreatedAt, log.Action })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(log => log.UserId).Select(group => group.First())
            .Select(log => new DocumentWatermarkEntry(
                string.IsNullOrWhiteSpace(log.FullName) ? log.Email ?? "Signed-in user" : log.FullName,
                log.CreatedAt,
                log.Action)).ToArray();
    }
}
