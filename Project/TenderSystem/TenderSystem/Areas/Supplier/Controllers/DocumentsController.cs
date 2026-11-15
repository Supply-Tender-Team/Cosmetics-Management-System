using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier + "," + Roles.EvaluationCommittee + "," + Roles.SourcingOfficer)]
public class DocumentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _files;

    public DocumentsController(ApplicationDbContext db, IFileStorageService files)
    {
        _db = db;
        _files = files;
    }

    [HttpGet]
    public async Task<IActionResult> Offer(int id, int? tenderId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var isSupplier = User.IsInRole(Roles.Supplier);
        var isCommitteeMember = User.IsInRole(Roles.EvaluationCommittee);
        var isSourcingOfficer = User.IsInRole(Roles.SourcingOfficer);
        var document = await _db.BidDocuments.AsNoTracking()
            .Where(x => x.Id == id &&
                ((isSupplier && x.Bid.SupplierProfile.UserId == userId) ||
                 (isCommitteeMember && x.Bid.Tender.CommitteeMembers.Any(member => member.UserId == userId)) ||
                 (isSourcingOfficer && tenderId.HasValue && x.Bid.TenderId == tenderId.Value
                     && x.Bid.SubmittedAt.HasValue
                     && (x.Bid.Status == Statuses.BidStatus.Submitted
                         || x.Bid.Status == Statuses.BidStatus.UnderReview
                         || x.Bid.Status == Statuses.BidStatus.Compliant
                         || x.Bid.Status == Statuses.BidStatus.NonCompliant))))
            .Select(x => new { x.FileName, x.ContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        return await BuildPreviewAsync("Offer document", document.FileName, document.ContentType,
            "BidDocument", id,
            Url.Action(nameof(OfferFile), new { id, tenderId }) ?? string.Empty,
            Url.Action("Download", "Offers", new { area = "Supplier", documentId = id, tenderId }) ?? string.Empty,
            cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> OfferFile(int id, int? tenderId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var isSupplier = User.IsInRole(Roles.Supplier);
        var isCommitteeMember = User.IsInRole(Roles.EvaluationCommittee);
        var isSourcingOfficer = User.IsInRole(Roles.SourcingOfficer);
        var document = await _db.BidDocuments.AsNoTracking()
            .Where(x => x.Id == id &&
                ((isSupplier && x.Bid.SupplierProfile.UserId == userId) ||
                 (isCommitteeMember && x.Bid.Tender.CommitteeMembers.Any(member => member.UserId == userId)) ||
                 (isSourcingOfficer && tenderId.HasValue && x.Bid.TenderId == tenderId.Value
                     && x.Bid.SubmittedAt.HasValue
                     && (x.Bid.Status == Statuses.BidStatus.Submitted
                         || x.Bid.Status == Statuses.BidStatus.UnderReview
                         || x.Bid.Status == Statuses.BidStatus.Compliant
                         || x.Bid.Status == Statuses.BidStatus.NonCompliant))))
            .Select(x => new { x.FilePath, x.ContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        return await OpenInlineAsync(document.FilePath, document.ContentType, cancellationToken);
    }

    [Authorize(Roles = Roles.Supplier)]
    [HttpGet]
    public async Task<IActionResult> Company(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(x => x.Id == id && x.SupplierProfile.UserId == userId)
            .Select(x => new { x.DocType, x.FilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        var extension = Path.GetExtension(document.FilePath);
        var fileName = document.DocType.Replace(' ', '-') + extension;
        var contentType = GetContentType(extension);
        return await BuildPreviewAsync("Company document", fileName, contentType,
            "SupplierDocument", id,
            Url.Action(nameof(CompanyFile), new { id }) ?? string.Empty,
            Url.Action("Download", "Profile", new { area = "Supplier", id }) ?? string.Empty,
            cancellationToken);
    }

    [Authorize(Roles = Roles.Supplier)]
    [HttpGet]
    public async Task<IActionResult> CompanyFile(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(x => x.Id == id && x.SupplierProfile.UserId == userId)
            .Select(x => new { x.FilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        return await OpenInlineAsync(document.FilePath, GetContentType(Path.GetExtension(document.FilePath)), cancellationToken);
    }

    private async Task<IActionResult> BuildPreviewAsync(string title, string fileName, string contentType,
        string documentKind, int documentId, string fileUrl, string downloadUrl,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var user = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.FullName, x.Email })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return Challenge();

        var viewedAt = DateTime.UtcNow;
        _db.DocumentAccessLogs.Add(new DocumentAccessLog
        {
            UserId = userId,
            DocumentKind = documentKind,
            DocumentId = documentId,
            Action = Statuses.DocumentAction.View,
            CreatedAt = viewedAt
        });
        await _db.SaveChangesAsync(cancellationToken);

        var normalizedContentType = (contentType ?? string.Empty).ToLowerInvariant();
        var canPreview = normalizedContentType is "application/pdf" or "image/png" or "image/jpeg";
        return View("Preview", new DocumentPreviewViewModel
        {
            Title = title,
            FileName = Path.GetFileName(fileName),
            ViewerName = string.IsNullOrWhiteSpace(user.FullName) ? user.Email ?? "Signed-in user" : user.FullName,
            ViewedAtUtc = viewedAt,
            FileUrl = fileUrl,
            DownloadUrl = downloadUrl,
            CanPreview = canPreview,
            IsImage = normalizedContentType is "image/png" or "image/jpeg"
        });
    }

    private async Task<IActionResult> OpenInlineAsync(string filePath, string contentType, CancellationToken cancellationToken)
    {
        var safeContentType = contentType is "application/pdf" or "image/png" or "image/jpeg"
            ? contentType
            : "application/octet-stream";
        if (safeContentType == "application/octet-stream") return BadRequest();

        try
        {
            var stream = await _files.OpenReadAsync(filePath, cancellationToken);
            return stream is null ? NotFound() : File(stream, safeContentType);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream"
    };
}
