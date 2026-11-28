using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Keeps the last AI result of a user (the Evaluation offer advisor and the Admin AI report) in the database, so a refresh, a later visit or an
/// application restart shows it again and nobody waits for the model twice. Saving again for the same user, kind and key replaces the result.
/// </summary>
public class AiSavedResultStore
{
    public const string Advisor = "advisor";
    public const string Report = "report";

    private readonly ApplicationDbContext _db;

    public AiSavedResultStore(ApplicationDbContext db) => _db = db;

    public Task<AiSavedResult?> GetAsync(string kind, string ownerUserId, string key, CancellationToken ct = default) =>
        _db.AiSavedResults.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == kind && x.OwnerUserId == ownerUserId && x.Key == key, ct);

    /// <summary>The saved result whose PDF link carries this token (only the owner can read it).</summary>
    public Task<AiSavedResult?> GetByTokenAsync(string kind, string ownerUserId, string token, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(token) ? Task.FromResult<AiSavedResult?>(null)
            : _db.AiSavedResults.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == kind && x.OwnerUserId == ownerUserId && x.Token == token, ct);

    public async Task SaveAsync(string kind, string ownerUserId, string key, string token, string fingerprint, string resultJson, string model, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var row = await _db.AiSavedResults.SingleOrDefaultAsync(x => x.Kind == kind && x.OwnerUserId == ownerUserId && x.Key == key, ct);
            if (row is null)
            {
                row = new AiSavedResult { Kind = kind, OwnerUserId = ownerUserId, Key = key };
                _db.AiSavedResults.Add(row);
            }
            row.Token = token;
            row.Fingerprint = fingerprint;
            row.ResultJson = resultJson;
            row.Model = model.Length > 200 ? model[..200] : model;
            row.CreatedAt = DateTime.UtcNow;
            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt == 1)
            {
                // The same user generated twice at the same moment (unique key): look the row up again and update it.
                _db.ChangeTracker.Clear();
            }
        }
    }
}
