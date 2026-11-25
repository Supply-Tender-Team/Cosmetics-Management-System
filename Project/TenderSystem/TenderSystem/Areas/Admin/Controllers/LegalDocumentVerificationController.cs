using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Governance;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class LegalDocumentVerificationController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly VerificationService _verification;
    private readonly AuditService _audit;

    public LegalDocumentVerificationController(ApplicationDbContext db, VerificationService verification, AuditService audit)
    {
        _db = db;
        _verification = verification;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, int documentId, CancellationToken ct)
    {
        var model = await LoadAsync(id, documentId, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(int id, int documentId, string? status, string? method,
        string? reason, int? expectedVerificationId, bool confirmed, CancellationToken ct)
    {
        if (id <= 0 || documentId <= 0) return NotFound();
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(reviewerId)) return Forbid();
        if (status is not (Statuses.VerificationStatus.Pending or Statuses.VerificationStatus.Verified or Statuses.VerificationStatus.Rejected)
            || method is not (Statuses.VerificationMethod.Manual or Statuses.VerificationMethod.OcrAssisted or Statuses.VerificationMethod.QrAssisted))
        {
            TempData["VerificationError"] = "Choose a supported status and verification method.";
            return RedirectToAction(nameof(Details), new { id, documentId });
        }

        reason = reason?.Trim();
        if (!confirmed || (status == Statuses.VerificationStatus.Rejected && string.IsNullOrWhiteSpace(reason))
            || reason?.Length > 1000 || expectedVerificationId < 0)
        {
            TempData["VerificationError"] = "Confirm the decision and provide a reason for rejection (up to 1000 characters).";
            return RedirectToAction(nameof(Details), new { id, documentId });
        }

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var belongsToSupplier = await _db.SupplierDocuments.AsNoTracking()
                .AnyAsync(d => d.Id == documentId && d.SupplierProfileId == id, ct);
            if (!belongsToSupplier) return NotFound();

            var latestId = await _db.DocumentVerifications.AsNoTracking()
                .Where(v => v.SupplierDocumentId == documentId)
                .MaxAsync(v => (int?)v.Id, ct);
            if (latestId != expectedVerificationId)
            {
                TempData["VerificationError"] = "This document has a newer decision. Review it before submitting again.";
                return RedirectToAction(nameof(Details), new { id, documentId });
            }

            var (row, error) = await _verification.RecordAsync(documentId, null, status, method, reason, reviewerId, ct);
            if (row is null)
            {
                TempData["VerificationError"] = error ?? "The decision could not be saved.";
                return RedirectToAction(nameof(Details), new { id, documentId });
            }

            await _audit.LogAsync("DocumentVerified", "DocumentVerification", row.Id.ToString(),
                $"DocumentId={documentId}; Status={status}; Method={method}");
            await transaction.CommitAsync(ct);
            TempData["VerificationSuccess"] = "Verification decision recorded.";
        }
        catch (Exception)
        {
            TempData["VerificationError"] = "The decision could not be saved. Please try again.";
        }

        return RedirectToAction(nameof(Details), new { id, documentId });
    }

    private async Task<LegalDocumentVerificationViewModel?> LoadAsync(int supplierId, int documentId, CancellationToken ct)
    {
        if (supplierId <= 0 || documentId <= 0) return null;
        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(d => d.Id == documentId && d.SupplierProfileId == supplierId)
            .Select(d => new { d.DocType, d.FilePath, d.ExpiryDate, CompanyName = d.SupplierProfile.CompanyName })
            .SingleOrDefaultAsync(ct);
        if (document is null) return null;

        var history = await _db.DocumentVerifications.AsNoTracking()
            .Where(v => v.SupplierDocumentId == documentId)
            .OrderByDescending(v => v.Id)
            .Select(v => new LegalDocumentVerificationHistoryViewModel
            {
                Id = v.Id,
                Status = v.Status,
                Method = v.Method,
                Reason = v.Reason,
                Reviewer = v.VerifiedByUser.FullName,
                VerifiedAt = v.VerifiedAt
            }).ToListAsync(ct);
        return new LegalDocumentVerificationViewModel
        {
            SupplierId = supplierId,
            DocumentId = documentId,
            CompanyName = document.CompanyName,
            DocumentType = document.DocType,
            ExpiryDate = document.ExpiryDate,
            CanView = document.FilePath.EndsWith(".pdf") || document.FilePath.EndsWith(".png")
                || document.FilePath.EndsWith(".jpg") || document.FilePath.EndsWith(".jpeg"),
            CurrentVerificationId = history.FirstOrDefault()?.Id,
            History = history
        };
    }
}
