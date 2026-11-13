using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Files;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier + "," + Roles.EvaluationCommittee)]
public class OffersController : Controller
{
    // OWNER: Dev 5

    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    public OffersController(ApplicationDbContext db, IFileStorageService fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
    }

    [Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var offers = await _db.Bids.AsNoTracking()
            .Where(x => x.SupplierProfile.UserId == userId)
            .OrderByDescending(x => x.SubmittedAt)
            .Select(x => new SupplierOfferDetailRow
            {
                Id = x.Id,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                TotalPrice = x.TotalPrice,
                Currency = x.Currency,
                SubmittedAt = x.SubmittedAt,
                Documents = x.BidDocuments.Select(document => new SupplierOfferDocumentRow
                {
                    Id = document.Id,
                    FileName = document.FileName,
                    SizeBytes = document.SizeBytes
                }).ToList()
            }).ToListAsync(cancellationToken);
        return View(new SupplierOffersViewModel { Offers = offers });
    }

    [Authorize(Roles = Roles.Supplier)]
    public IActionResult Create() => View();

    [Authorize(Roles = Roles.Supplier)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Upload(int bidId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Index));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var bid = await _db.Bids.SingleOrDefaultAsync(x => x.Id == bidId && x.SupplierProfile.UserId == userId, cancellationToken);
        if (bid is null) return NotFound();
        if (!string.Equals(bid.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Files can only be added to a draft offer.");

        try
        {
            await using var stream = file.OpenReadStream();
            var stored = await _fileStorage.SaveAsync(stream, file.FileName, file.ContentType, cancellationToken);
            _db.BidDocuments.Add(new Data.Entities.Bids.BidDocument
            {
                BidId = bid.Id,
                DocType = "Offer attachment",
                FileName = stored.OriginalName,
                FilePath = stored.RelativePath,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                UploadedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "The file was uploaded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(int documentId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isSupplier = User.IsInRole(Roles.Supplier);
        var isCommitteeMember = User.IsInRole(Roles.EvaluationCommittee);
        var document = await _db.BidDocuments.AsNoTracking()
            .Where(x => x.Id == documentId &&
                ((isSupplier && x.Bid.SupplierProfile.UserId == userId) ||
                 (isCommitteeMember && x.Bid.Tender.CommitteeMembers.Any(member => member.UserId == userId))))
            .Select(x => new { x.FilePath, x.FileName, x.ContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        try
        {
            var stream = await _fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
            if (stream is null) return NotFound();
            return File(stream, string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType, Path.GetFileName(document.FileName));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
