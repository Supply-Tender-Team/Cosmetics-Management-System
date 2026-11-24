using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Governance;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Governance;

/// <summary>
/// Records verification decisions about supplier documents and guarantees. The table only grows, so a changed decision keeps the old one as history.
/// The CALLER checks the role: the Admin for supplier documents, Sourcing for guarantees. Nothing here writes "Verified" by itself.
/// </summary>
public class VerificationService
{
    private static readonly string[] AllowedStatuses = [Statuses.VerificationStatus.Pending, Statuses.VerificationStatus.Verified, Statuses.VerificationStatus.Rejected];
    private static readonly string[] AllowedMethods = [Statuses.VerificationMethod.Manual, Statuses.VerificationMethod.OcrAssisted, Statuses.VerificationMethod.QrAssisted];

    private readonly ApplicationDbContext _db;

    public VerificationService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Adds a decision about ONE target: give either supplierDocumentId or bidGuaranteeId, never both. Returns the new row, or an error text
    /// (the row is null then and nothing was saved).
    /// </summary>
    public async Task<(DocumentVerification? Row, string? Error)> RecordAsync(
        int? supplierDocumentId, int? bidGuaranteeId, string status, string method, string? reason, string userId, CancellationToken ct = default)
    {
        if ((supplierDocumentId is null) == (bidGuaranteeId is null)) return (null, "Choose exactly one supplier document or one guarantee.");
        if (string.IsNullOrWhiteSpace(userId)) return (null, "The reviewer is required.");
        if (!AllowedStatuses.Contains(status)) return (null, "The status must be Pending, Verified or Rejected.");
        if (!AllowedMethods.Contains(method)) return (null, "The method must be Manual, OcrAssisted or QrAssisted.");
        reason = reason?.Trim() ?? string.Empty;
        if (status == Statuses.VerificationStatus.Rejected && reason.Length == 0) return (null, "A reason is required when the status is Rejected.");
        if (reason.Length > 1000) return (null, "The reason is too long (1000 characters at most).");

        var targetExists = supplierDocumentId is not null
            ? await _db.SupplierDocuments.AsNoTracking().AnyAsync(d => d.Id == supplierDocumentId, ct)
            : await _db.BidGuarantees.AsNoTracking().AnyAsync(g => g.Id == bidGuaranteeId, ct);
        if (!targetExists) return (null, "The supplier document or guarantee was not found.");

        var row = new DocumentVerification
        {
            SupplierDocumentId = supplierDocumentId, BidGuaranteeId = bidGuaranteeId,
            Status = status, Method = method, Reason = reason, VerifiedByUserId = userId, VerifiedAt = DateTime.UtcNow
        };
        _db.DocumentVerifications.Add(row);
        await _db.SaveChangesAsync(ct);
        return (row, null);
    }

    /// <summary>
    /// The CURRENT decision of every document and guarantee: the newest row of each target. A document or guarantee that is not in this list has
    /// no verification recorded and must be shown as such, never as Verified.
    /// </summary>
    public IQueryable<DocumentVerification> Current() =>
        _db.DocumentVerifications.AsNoTracking().Where(v => !_db.DocumentVerifications.Any(newer => newer.Id > v.Id
            && newer.SupplierDocumentId == v.SupplierDocumentId && newer.BidGuaranteeId == v.BidGuaranteeId));
}
