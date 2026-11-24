using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TenderSystem.Areas.Supplier.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Files;
using TenderSystem.Services.Notifications;
using TenderSystem.Services.Ocr;

namespace TenderSystem.Areas.Supplier.Controllers;

[Area("Supplier")]
[Authorize(Roles = Roles.Supplier + "," + Roles.EvaluationCommittee + "," + Roles.SourcingOfficer)]
public class OffersController : Controller
{
    // OWNER: Dev 5

    private const string DraftStatus = "Draft";
    private const string SubmittedStatus = "Submitted";
    private const string WithdrawnStatus = "Withdrawn";
    private const string AcceptedInvitationStatus = "Accepted";
    private const string PublishedTenderStatus = "Published";
    private const string OpenTenderStatus = "Open";
    private const string SaveDraftAction = "Draft";
    private const string SubmitOfferAction = "Submit";
    private const decimal SupplierTaxRate = 16m;
    private const string BankGuaranteeType = "BankGuarantee";
    private const string JudicialGuaranteeType = "JudicialGuarantee";

    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly AuditService _audit;
    private readonly NotificationService _notify;
    private readonly GuaranteeDocumentReader _guaranteeDocumentReader;
    private readonly ILogger<OffersController> _logger;
    private readonly IWebHostEnvironment _environment;

    public OffersController(ApplicationDbContext db, IFileStorageService fileStorage, AuditService audit,
        NotificationService notify, GuaranteeDocumentReader guaranteeDocumentReader,
        ILogger<OffersController> logger, IWebHostEnvironment environment)
    {
        _db = db;
        _fileStorage = fileStorage;
        _audit = audit;
        _notify = notify;
        _guaranteeDocumentReader = guaranteeDocumentReader;
        _logger = logger;
        _environment = environment;
    }

    [Authorize(Roles = Roles.Supplier)]
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> ReadGuaranteeDocument(IFormFile? file, string? guaranteeType,
        CancellationToken cancellationToken)
    {
        if (await GetCurrentSupplierIdAsync(cancellationToken) is null) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { message = "Choose a guarantee file first." });
        if (guaranteeType is not (BankGuaranteeType or JudicialGuaranteeType))
            return BadRequest(new { message = "The guarantee type is invalid." });

        try
        {
            var result = await _guaranteeDocumentReader.ReadAsync(file, cancellationToken);
            return Json(new { fields = result.Fields });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Guarantee OCR failed for uploaded file type {FileExtension} ({FileSize} bytes).",
                Path.GetExtension(file.FileName), file.Length);
            var message = _environment.IsDevelopment()
                ? $"OCR diagnostic: {ex.GetType().Name}: {ex.Message}"
                : "We could not read this file. You can still enter the guarantee details manually.";
            return BadRequest(new { message });
        }
    }

    [Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, int? tenderId = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        // Previously sent result notices link here by tender number's record ID.
        if (tenderId.HasValue)
        {
            var bidId = await _db.Bids.AsNoTracking()
                .Where(b => b.TenderId == tenderId && b.SupplierProfile.UserId == userId
                    && b.SubmittedAt.HasValue && b.Status != Statuses.BidStatus.Draft
                    && b.Status != Statuses.BidStatus.Withdrawn)
                .OrderByDescending(b => b.SubmittedAt)
                .ThenByDescending(b => b.Id)
                .Select(b => (int?)b.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (bidId.HasValue)
                return RedirectToAction("Index", "OfferDetails", new { id = bidId.Value });
        }

        var offers = await _db.Bids.AsNoTracking()
            .Where(x => x.SupplierProfile.UserId == userId)
            .OrderByDescending(x => x.SubmittedAt)
            .Select(x => new SupplierOfferDetailRow
            {
                Id = x.Id,
                TenderId = x.TenderId,
                TenderNumber = x.Tender.TenderNumber,
                TenderTitle = x.Tender.Title,
                Status = x.Status,
                TotalPrice = x.TotalPrice,
                Currency = x.Currency,
                SubmittedAt = x.SubmittedAt,
                CanWithdraw = x.Status == SubmittedStatus
                    && x.Tender.SubmissionDeadline > DateTime.UtcNow
                    && (x.Tender.Status == PublishedTenderStatus || x.Tender.Status == OpenTenderStatus),
                Documents = x.BidDocuments.Select(document => new SupplierOfferDocumentRow
                {
                    Id = document.Id,
                    FileName = document.FileName,
                    SizeBytes = document.SizeBytes,
                    IsGuaranteeDocument = document.BidGuaranteeId.HasValue
                }).ToList()
            }).ToListAsync(cancellationToken);
        return View(new SupplierOffersViewModel { Offers = offers });
    }

    [Authorize(Roles = Roles.Supplier)]
    [HttpGet]
    public async Task<IActionResult> Create(int tenderId, CancellationToken cancellationToken)
    {
        var supplierId = await GetCurrentSupplierIdAsync(cancellationToken);
        if (supplierId is null) return Forbid();

        var tender = await GetEligibleTenderAsync(tenderId, supplierId.Value, cancellationToken);
        if (tender is null || !IsTenderOpen(tender, DateTime.UtcNow)) return NotFound();

        var existingBid = await _db.Bids.AsNoTracking()
            .Include(x => x.BidItems)
            .Include(x => x.BidDocuments)
            .Include(x => x.BidGuarantees).ThenInclude(x => x.BidDocuments)
            .FirstOrDefaultAsync(x => x.TenderId == tender.Id && x.SupplierProfileId == supplierId.Value, cancellationToken);
        if (existingBid is not null && existingBid.Status != DraftStatus && existingBid.Status != WithdrawnStatus)
        {
            TempData["Error"] = "An offer has already been submitted for this tender.";
            return RedirectToAction(nameof(Index));
        }

        return View(BuildOfferModel(tender, existingBid));
    }

    [Authorize(Roles = Roles.Supplier)]
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Create(CreateSupplierOfferViewModel model, string submitAction, IFormFile? document,
        CancellationToken cancellationToken)
    {
        if (submitAction != SaveDraftAction && submitAction != SubmitOfferAction) return BadRequest();
        model.Items ??= [];
        model.Guarantees ??= [];

        var supplierId = await GetCurrentSupplierIdAsync(cancellationToken);
        if (supplierId is null) return Forbid();

        var tender = await GetEligibleTenderAsync(model.TenderId, supplierId.Value, cancellationToken, tracked: true);
        if (tender is null || !IsTenderOpen(tender, DateTime.UtcNow)) return NotFound();

        var expectedItems = tender.Requisition.PurchaseRequisitionItems.OrderBy(x => x.Id).ToList();
        var postedItemIds = model.Items.Select(x => x.RequisitionItemId).OrderBy(x => x).ToArray();
        var expectedItemIds = expectedItems.Select(x => x.Id).OrderBy(x => x).ToArray();
        if (!postedItemIds.SequenceEqual(expectedItemIds))
            ModelState.AddModelError(nameof(model.Items), "The offer items do not match the tender request.");

        var isSubmit = submitAction == SubmitOfferAction;
        if (isSubmit)
        {
            if (expectedItems.Count == 0)
                ModelState.AddModelError(nameof(model.Items), "This tender has no requested items to include in an offer.");
            if (string.IsNullOrWhiteSpace(model.Currency) || model.Currency.Trim().Length != 3)
                ModelState.AddModelError(nameof(model.Currency), "Enter a 3-letter currency code.");
            if (model.DeliveryDays <= 0)
                ModelState.AddModelError(nameof(model.DeliveryDays), "Enter the delivery time in days.");
            if (model.ValidityDays <= 0)
                ModelState.AddModelError(nameof(model.ValidityDays), "Enter how many days the offer is valid.");
            if (string.IsNullOrWhiteSpace(model.PaymentTerms))
                ModelState.AddModelError(nameof(model.PaymentTerms), "Enter the payment terms.");
            if (string.IsNullOrWhiteSpace(model.TechnicalSummary))
                ModelState.AddModelError(nameof(model.TechnicalSummary), "Enter a technical summary.");
            if (model.Items.Any(x => !x.UnitPrice.HasValue || x.UnitPrice.Value <= 0))
                ModelState.AddModelError(nameof(model.Items), "Enter a unit price above zero for every item.");
        }

        var existingGuaranteeTypesWithDocuments = await _db.BidGuarantees.AsNoTracking()
            .Where(guarantee => guarantee.Bid.TenderId == tender.Id
                && guarantee.Bid.SupplierProfileId == supplierId.Value
                && guarantee.BidDocuments.Any())
            .Select(guarantee => guarantee.GuaranteeType)
            .ToListAsync(cancellationToken);
        ValidateSupplierUploads(model, isSubmit, existingGuaranteeTypesWithDocuments);

        if (!ModelState.IsValid)
        {
            var existingDraft = await _db.Bids.AsNoTracking().Include(x => x.BidItems)
                .Include(x => x.BidDocuments)
                .Include(x => x.BidGuarantees).ThenInclude(x => x.BidDocuments)
                .FirstOrDefaultAsync(x => x.TenderId == tender.Id && x.SupplierProfileId == supplierId.Value && x.Status == DraftStatus, cancellationToken);
            return View(BuildOfferModel(tender, existingDraft, model));
        }

        var bid = await _db.Bids.Include(x => x.BidItems)
            .Include(x => x.BidDocuments)
            .Include(x => x.BidGuarantees).ThenInclude(x => x.BidDocuments)
            .FirstOrDefaultAsync(x => x.TenderId == tender.Id && x.SupplierProfileId == supplierId.Value, cancellationToken);
        if (bid is not null && bid.Status != DraftStatus && bid.Status != WithdrawnStatus)
        {
            TempData["Error"] = "An offer has already been submitted for this tender.";
            return RedirectToAction(nameof(Index));
        }

        if (bid is null)
        {
            bid = new Bid { TenderId = tender.Id, SupplierProfileId = supplierId.Value };
            _db.Bids.Add(bid);
        }

        var submittedItems = model.Items.ToDictionary(x => x.RequisitionItemId);
        bid.Status = DraftStatus;
        bid.Currency = string.IsNullOrWhiteSpace(model.Currency) ? "USD" : model.Currency.Trim().ToUpperInvariant();
        bid.DeliveryDays = model.DeliveryDays;
        bid.ValidityDays = model.ValidityDays;
        bid.PaymentTerms = model.PaymentTerms?.Trim() ?? string.Empty;
        bid.TechnicalSummary = model.TechnicalSummary?.Trim() ?? string.Empty;
        bid.SubmittedAt = isSubmit ? DateTime.UtcNow : null;
        var retainedItemIds = new HashSet<int>();
        foreach (var item in expectedItems)
        {
            var offerItem = submittedItems[item.Id];
            var bidItem = bid.BidItems.FirstOrDefault(x => x.RequisitionItemId == item.Id);
            if (bidItem is null)
            {
                bidItem = new BidItem { RequisitionItemId = item.Id };
                bid.BidItems.Add(bidItem);
            }

            retainedItemIds.Add(item.Id);
            bidItem.Quantity = item.Quantity;
            bidItem.UnitPrice = offerItem.UnitPrice ?? 0m;
            bidItem.TaxRate = offerItem.UnitPrice.HasValue ? SupplierTaxRate : 0m;
            bidItem.TaxAmount = bidItem.TaxRate == 0m
                ? 0m
                : decimal.Round(bidItem.UnitPrice * bidItem.Quantity * bidItem.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            bidItem.OfferedSpecifications = offerItem.OfferedSpecifications?.Trim() ?? string.Empty;
        }

        // Keep BidItem IDs stable so previously uploaded sample images remain linked when a draft is edited.
        foreach (var staleItem in bid.BidItems.Where(x => !retainedItemIds.Contains(x.RequisitionItemId)).ToList())
        {
            foreach (var staleDocument in bid.BidDocuments.Where(x => x.BidItemId == staleItem.Id).ToList())
                _db.BidDocuments.Remove(staleDocument);
            _db.BidItems.Remove(staleItem);
        }

        ApplyGuaranteeInputs(bid, model.Guarantees);
        bid.TotalPrice = bid.BidItems.Sum(x => x.UnitPrice * x.Quantity + x.TaxAmount + x.ShippingCost + (x.InstallationCost ?? 0m));

        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            if (document is { Length: > 0 })
                await SaveBidDocumentAsync(bid, document, "Offer attachment", null, null, cancellationToken);

            foreach (var itemInput in model.Items)
            {
                var bidItem = bid.BidItems.Single(x => x.RequisitionItemId == itemInput.RequisitionItemId);
                foreach (var image in itemInput.SampleImages.Where(x => x is { Length: > 0 }))
                    await SaveBidDocumentAsync(bid, image, "SampleImage", bidItem.Id, null, cancellationToken);
                foreach (var attachment in itemInput.SupportingDocuments.Where(x => x is { Length: > 0 }))
                    await SaveBidDocumentAsync(bid, attachment, "ItemDocument", bidItem.Id, null, cancellationToken);
            }

            foreach (var guaranteeInput in model.Guarantees.Where(GuaranteeHasInput))
            {
                var guarantee = bid.BidGuarantees.Single(x => string.Equals(x.GuaranteeType, guaranteeInput.GuaranteeType, StringComparison.OrdinalIgnoreCase));
                if (guaranteeInput.Document is { Length: > 0 })
                    await SaveBidDocumentAsync(bid, guaranteeInput.Document, guarantee.GuaranteeType, null, guarantee.Id, cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            bid.SubmittedAt = null;
            await _db.SaveChangesAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, ex.Message + " Your offer is saved as a draft; correct the file and continue.");
            return View(BuildOfferModel(tender, bid, model));
        }

        bid.Status = isSubmit ? SubmittedStatus : DraftStatus;
        bid.TotalPrice = bid.BidItems.Sum(x => x.UnitPrice * x.Quantity + x.TaxAmount + x.ShippingCost + (x.InstallationCost ?? 0m));
        await _db.SaveChangesAsync(cancellationToken);

        if (document is { Length: > 0 } || model.Items.Any(x => x.SampleImages.Any(f => f.Length > 0)
            || x.SupportingDocuments.Any(f => f.Length > 0)) || model.Guarantees.Any(x => x.Document is { Length: > 0 }))
            await _audit.LogAsync("Uploaded", "BidDocument", bid.Id.ToString(), $"Offer files added to bid {bid.Id}.");
        if (isSubmit)
        {
            await _audit.LogAsync("Submitted", "Bid", bid.Id.ToString(), "Offer submitted");
            await _notify.NotifyRoleAsync(Roles.SourcingOfficer, "OfferReceived", "New offer received",
                "A supplier submitted an offer.", "/Sourcing/Offers");
            TempData["Success"] = "Your offer was submitted.";
        }
        else
        {
            await _audit.LogAsync("DraftSaved", "Bid", bid.Id.ToString(), "Offer draft saved.");
            TempData["Success"] = "Your offer draft was saved.";
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Supplier)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int bidId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var bid = await _db.Bids.Include(x => x.Tender)
            .FirstOrDefaultAsync(x => x.Id == bidId && x.SupplierProfile.UserId == userId, cancellationToken);
        if (bid is null) return NotFound();
        if (bid.Status != SubmittedStatus || !IsTenderOpen(bid.Tender, DateTime.UtcNow))
        {
            TempData["Error"] = "This offer cannot be withdrawn now.";
            return RedirectToAction(nameof(Index));
        }

        bid.Status = WithdrawnStatus;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Withdrawn", "Bid", bid.Id.ToString(), "Offer withdrawn");
        TempData["Success"] = "Your offer was withdrawn.";
        return RedirectToAction(nameof(Index));
    }

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
            var attachment = new Data.Entities.Bids.BidDocument
            {
                BidId = bid.Id,
                DocType = "Offer attachment",
                FileName = stored.OriginalName,
                FilePath = stored.RelativePath,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                UploadedAt = DateTime.UtcNow
            };
            _db.BidDocuments.Add(attachment);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Uploaded", "BidDocument", attachment.Id.ToString(), $"Offer attachment added to bid {bid.Id}.");
            TempData["Success"] = "The file was uploaded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(int documentId, int? tenderId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isSupplier = User.IsInRole(Roles.Supplier);
        var isCommitteeMember = User.IsInRole(Roles.EvaluationCommittee);
        var isSourcingOfficer = User.IsInRole(Roles.SourcingOfficer);
        var document = await _db.BidDocuments.AsNoTracking()
            .Where(x => x.Id == documentId &&
                ((isSupplier && x.Bid.SupplierProfile.UserId == userId) ||
                 (isCommitteeMember && x.Bid.Tender.CommitteeMembers.Any(member => member.UserId == userId)) ||
                 (isSourcingOfficer && tenderId.HasValue && x.Bid.TenderId == tenderId.Value
                     && x.Bid.SubmittedAt.HasValue
                     && (x.Bid.Status == Statuses.BidStatus.Submitted
                         || x.Bid.Status == Statuses.BidStatus.UnderReview
                         || x.Bid.Status == Statuses.BidStatus.Compliant
                         || x.Bid.Status == Statuses.BidStatus.NonCompliant))))
            .Select(x => new { x.FilePath, x.FileName, x.ContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        try
        {
            await using var stream = await _fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
            if (stream is null) return NotFound();

            var viewer = await _db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new { user.FullName, user.Email })
                .SingleOrDefaultAsync(cancellationToken);
            if (viewer is null) return Challenge();

            var downloadedAt = DateTime.UtcNow;
            var viewerName = string.IsNullOrWhiteSpace(viewer.FullName) ? viewer.Email ?? "Signed-in user" : viewer.FullName;
            var webRoot = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
            var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(
                _db, userId, "BidDocument", documentId, Statuses.DocumentAction.Download, downloadedAt, cancellationToken);
            var watermarkedContent = await DocumentWatermarkService.CreateWatermarkedCopyAsync(
                stream, document.FileName, viewerName, downloadedAt, webRoot, cancellationToken, history);

            if (string.Equals(document.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                Response.Headers.CacheControl = "no-store";
            return File(watermarkedContent, string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType, Path.GetFileName(document.FileName));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private async Task<int?> GetCurrentSupplierIdAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return null;

        return await _db.SupplierProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<Tender?> GetEligibleTenderAsync(int tenderId, int supplierId,
        CancellationToken cancellationToken, bool tracked = false)
    {
        IQueryable<Tender> query = _db.Tenders;
        if (!tracked) query = query.AsNoTracking();

        return await query
            .Include(x => x.Requisition)
            .ThenInclude(x => x.PurchaseRequisitionItems)
            .SingleOrDefaultAsync(x => x.Id == tenderId
                && x.Invitations.Any(invitation => invitation.SupplierProfileId == supplierId
                    && invitation.Status == AcceptedInvitationStatus), cancellationToken);
    }

    private static bool IsTenderOpen(Tender tender, DateTime now) =>
        tender.SubmissionDeadline > now
        && (tender.Status == PublishedTenderStatus || tender.Status == OpenTenderStatus);

    private static CreateSupplierOfferViewModel BuildOfferModel(Tender tender, Bid? draft,
        CreateSupplierOfferViewModel? posted = null)
    {
        var postedItems = posted?.Items?
            .GroupBy(x => x.RequisitionItemId)
            .ToDictionary(group => group.Key, group => group.First());

        var draftGuarantees = draft?.BidGuarantees.GroupBy(x => x.GuaranteeType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, BidGuarantee>(StringComparer.OrdinalIgnoreCase);
        var model = new CreateSupplierOfferViewModel
        {
            TenderId = tender.Id,
            TenderNumber = tender.TenderNumber,
            TenderTitle = tender.Title,
            SubmissionDeadline = tender.SubmissionDeadline,
            Currency = posted?.Currency ?? draft?.Currency ?? "USD",
            DeliveryDays = posted?.DeliveryDays ?? draft?.DeliveryDays ?? 0,
            ValidityDays = posted?.ValidityDays ?? draft?.ValidityDays ?? 0,
            PaymentTerms = posted?.PaymentTerms ?? draft?.PaymentTerms ?? string.Empty,
            TechnicalSummary = posted?.TechnicalSummary ?? draft?.TechnicalSummary ?? string.Empty,
            Items = tender.Requisition.PurchaseRequisitionItems.OrderBy(item => item.Id).Select(item =>
            {
                SupplierOfferItemInputViewModel? postedItem = null;
                if (postedItems is not null) postedItems.TryGetValue(item.Id, out postedItem);
                var draftItem = draft?.BidItems.FirstOrDefault(x => x.RequisitionItemId == item.Id);
                return new SupplierOfferItemInputViewModel
                {
                    RequisitionItemId = item.Id,
                    BidItemId = draftItem?.Id,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    RequestedSpecifications = item.Specifications,
                    UnitPrice = postedItem?.UnitPrice ?? draftItem?.UnitPrice,
                    OfferedSpecifications = postedItem?.OfferedSpecifications ?? draftItem?.OfferedSpecifications ?? string.Empty
                };
            }).ToList(),
            Guarantees = new[] { BankGuaranteeType, JudicialGuaranteeType }.Select(type =>
            {
                var postedGuarantee = posted?.Guarantees?.FirstOrDefault(x => string.Equals(x.GuaranteeType, type, StringComparison.OrdinalIgnoreCase));
                draftGuarantees.TryGetValue(type, out var draftGuarantee);
                return new SupplierOfferGuaranteeInputViewModel
                {
                    GuaranteeType = type,
                    BidGuaranteeId = draftGuarantee?.Id,
                    ReferenceNumber = postedGuarantee?.ReferenceNumber ?? draftGuarantee?.ReferenceNumber,
                    IssuerName = postedGuarantee?.IssuerName ?? draftGuarantee?.IssuerName,
                    BeneficiaryName = postedGuarantee?.BeneficiaryName ?? draftGuarantee?.BeneficiaryName ?? string.Empty,
                    Amount = postedGuarantee?.Amount ?? draftGuarantee?.Amount,
                    Currency = postedGuarantee?.Currency ?? draftGuarantee?.Currency ?? "JOD",
                    IssuedAt = postedGuarantee?.IssuedAt ?? draftGuarantee?.IssuedAt,
                    ExpiresAt = postedGuarantee?.ExpiresAt ?? draftGuarantee?.ExpiresAt,
                    GuarantorName = postedGuarantee?.GuarantorName ?? draftGuarantee?.GuarantorName,
                    GuarantorNationalNumber = postedGuarantee?.GuarantorNationalNumber ?? draftGuarantee?.GuarantorNationalNumber,
                    GuarantorAddress = postedGuarantee?.GuarantorAddress ?? draftGuarantee?.GuarantorAddress,
                    GuarantorPhone = postedGuarantee?.GuarantorPhone ?? draftGuarantee?.GuarantorPhone
                };
            }).ToList(),
            ExistingDocuments = draft?.BidDocuments.Select(document => new SupplierOfferExistingDocumentViewModel
            {
                Id = document.Id,
                FileName = document.FileName,
                DocType = document.DocType,
                BidItemId = document.BidItemId,
                BidGuaranteeId = document.BidGuaranteeId,
                SizeBytes = document.SizeBytes
            }).ToList() ?? []
        };

        return model;
    }

    private void ValidateSupplierUploads(CreateSupplierOfferViewModel model, bool isSubmit,
        IReadOnlyCollection<string> existingGuaranteeTypesWithDocuments)
    {
        var uploads = new List<IFormFile>();
        foreach (var item in model.Items)
        {
            item.SampleImages ??= [];
            item.SupportingDocuments ??= [];
            uploads.AddRange(item.SampleImages.Where(file => file is { Length: > 0 }));
            uploads.AddRange(item.SupportingDocuments.Where(file => file is { Length: > 0 }));
        }
        uploads.AddRange(model.Guarantees.Select(guarantee => guarantee.Document).Where(file => file is { Length: > 0 }).Cast<IFormFile>());
        if (uploads.Sum(file => file.Length) > 50L * 1024 * 1024)
            ModelState.AddModelError(string.Empty, "The total size of uploaded files cannot exceed 50 MB.");

        for (var index = 0; index < model.Items.Count; index++)
        {
            var item = model.Items[index];
            foreach (var image in item.SampleImages.Where(file => file is { Length: > 0 }))
            {
                var extension = Path.GetExtension(image.FileName);
                if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    ModelState.AddModelError($"Items[{index}].SampleImages", "Sample images must be JPG or PNG files.");
            }
        }

        if (model.Guarantees.Select(guarantee => guarantee.GuaranteeType).Distinct(StringComparer.OrdinalIgnoreCase).Count() != model.Guarantees.Count)
            ModelState.AddModelError(nameof(model.Guarantees), "Each guarantee type can be entered only once.");

        foreach (var guarantee in model.Guarantees)
        {
            if (!string.Equals(guarantee.GuaranteeType, BankGuaranteeType, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(guarantee.GuaranteeType, JudicialGuaranteeType, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(model.Guarantees), "The guarantee type is invalid.");

            if (GuaranteeHasInput(guarantee) && guarantee.Amount is <= 0)
                ModelState.AddModelError(nameof(model.Guarantees), "A guarantee amount must be greater than zero.");
            if (isSubmit && GuaranteeHasInput(guarantee) && string.IsNullOrWhiteSpace(guarantee.BeneficiaryName))
                ModelState.AddModelError(nameof(model.Guarantees), "Enter the beneficiary for each guarantee you provide.");
            if (isSubmit)
            {
                if (!guarantee.Amount.HasValue || guarantee.Amount <= 0)
                    ModelState.AddModelError(nameof(model.Guarantees), "Enter an amount for each bank and judicial guarantee.");
                if (string.IsNullOrWhiteSpace(guarantee.BeneficiaryName))
                    ModelState.AddModelError(nameof(model.Guarantees), "Enter the beneficiary for each bank and judicial guarantee.");
                if (guarantee.Document is not { Length: > 0 }
                    && !existingGuaranteeTypesWithDocuments.Contains(guarantee.GuaranteeType, StringComparer.OrdinalIgnoreCase))
                    ModelState.AddModelError(nameof(model.Guarantees), "Upload a document for each bank and judicial guarantee.");

                if (string.Equals(guarantee.GuaranteeType, BankGuaranteeType, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(guarantee.ReferenceNumber) || string.IsNullOrWhiteSpace(guarantee.IssuerName)))
                    ModelState.AddModelError(nameof(model.Guarantees), "Enter the bank guarantee reference number and issuer.");
                if (string.Equals(guarantee.GuaranteeType, JudicialGuaranteeType, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(guarantee.GuarantorName)
                        || string.IsNullOrWhiteSpace(guarantee.GuarantorNationalNumber)
                        || string.IsNullOrWhiteSpace(guarantee.GuarantorAddress)
                        || string.IsNullOrWhiteSpace(guarantee.GuarantorPhone)))
                    ModelState.AddModelError(nameof(model.Guarantees), "Enter the judicial guarantor's name, national number, address, and phone.");
            }
            if (guarantee.IssuedAt.HasValue && guarantee.ExpiresAt.HasValue && guarantee.ExpiresAt < guarantee.IssuedAt)
                ModelState.AddModelError(nameof(model.Guarantees), "A guarantee expiry date cannot be before its issue date.");
            if (GuaranteeHasInput(guarantee) && !string.IsNullOrWhiteSpace(guarantee.Currency) && guarantee.Currency.Trim().Length != 3)
                ModelState.AddModelError(nameof(model.Guarantees), "Guarantee currency must be a 3-letter code.");
        }

        if (isSubmit)
        {
            foreach (var requiredType in new[] { BankGuaranteeType, JudicialGuaranteeType })
            {
                if (!model.Guarantees.Any(guarantee => string.Equals(guarantee.GuaranteeType, requiredType, StringComparison.OrdinalIgnoreCase)))
                    ModelState.AddModelError(nameof(model.Guarantees), "Both bank and judicial guarantee details are required before submitting the offer.");
            }
        }
    }

    private static bool GuaranteeHasInput(SupplierOfferGuaranteeInputViewModel guarantee) =>
        guarantee.Amount.HasValue || !string.IsNullOrWhiteSpace(guarantee.ReferenceNumber)
        || !string.IsNullOrWhiteSpace(guarantee.IssuerName) || !string.IsNullOrWhiteSpace(guarantee.BeneficiaryName)
        || guarantee.IssuedAt.HasValue || guarantee.ExpiresAt.HasValue || !string.IsNullOrWhiteSpace(guarantee.GuarantorName)
        || !string.IsNullOrWhiteSpace(guarantee.GuarantorNationalNumber) || !string.IsNullOrWhiteSpace(guarantee.GuarantorAddress)
        || !string.IsNullOrWhiteSpace(guarantee.GuarantorPhone) || guarantee.Document is { Length: > 0 };

    private static void ApplyGuaranteeInputs(Bid bid, IEnumerable<SupplierOfferGuaranteeInputViewModel> inputs)
    {
        foreach (var input in inputs.Where(GuaranteeHasInput))
        {
            var type = string.Equals(input.GuaranteeType, BankGuaranteeType, StringComparison.OrdinalIgnoreCase)
                ? BankGuaranteeType : JudicialGuaranteeType;
            var guarantee = bid.BidGuarantees.FirstOrDefault(x => x.GuaranteeType == type);
            if (guarantee is null)
            {
                guarantee = new BidGuarantee { GuaranteeType = type, Currency = "JOD" };
                bid.BidGuarantees.Add(guarantee);
            }

            guarantee.ReferenceNumber = input.ReferenceNumber?.Trim();
            guarantee.IssuerName = input.IssuerName?.Trim();
            guarantee.BeneficiaryName = input.BeneficiaryName?.Trim() ?? string.Empty;
            guarantee.Amount = input.Amount ?? 0m;
            guarantee.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "JOD" : input.Currency.Trim().ToUpperInvariant();
            guarantee.IssuedAt = input.IssuedAt;
            guarantee.ExpiresAt = input.ExpiresAt;
            guarantee.GuarantorName = input.GuarantorName?.Trim();
            guarantee.GuarantorNationalNumber = input.GuarantorNationalNumber?.Trim();
            guarantee.GuarantorAddress = input.GuarantorAddress?.Trim();
            guarantee.GuarantorPhone = input.GuarantorPhone?.Trim();
        }
    }

    private async Task SaveBidDocumentAsync(Bid bid, IFormFile file, string documentType,
        int? bidItemId, int? bidGuaranteeId, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var contentType = GetSafeContentType(Path.GetExtension(file.FileName));
        var stored = await _fileStorage.SaveAsync(stream, file.FileName, contentType, cancellationToken);
        var document = new BidDocument
        {
            BidId = bid.Id,
            DocType = documentType,
            FileName = stored.OriginalName,
            FilePath = stored.RelativePath,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            UploadedAt = DateTime.UtcNow,
            BidItemId = bidItemId,
            BidGuaranteeId = bidGuaranteeId
        };
        _db.BidDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string GetSafeContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    #region SUP-06 - Personal offer summary

    [Authorize(Roles = Roles.Supplier)]
    [HttpGet]
    public async Task<IActionResult> SummaryReport(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var offers = await _db.Bids.AsNoTracking()
            .Where(bid => bid.SupplierProfile.UserId == userId)
            .OrderByDescending(bid => bid.SubmittedAt)
            .Select(bid => new
            {
                bid.Tender.TenderNumber,
                bid.Tender.Title,
                bid.Status,
                bid.TotalPrice,
                bid.Currency,
                bid.DeliveryDays,
                bid.ValidityDays,
                bid.PaymentTerms,
                bid.TechnicalSummary,
                bid.SubmittedAt
            })
            .ToListAsync(cancellationToken);

        var rows = new List<string>
        {
            string.Join(",", new[]
            {
                "Tender number", "Tender title", "Status", "Total price", "Currency",
                "Delivery days", "Validity days", "Payment terms", "Technical summary", "Submitted at (UTC)"
            }.Select(CsvField))
        };

        foreach (var offer in offers)
        {
            rows.Add(string.Join(",", new[]
            {
                CsvField(offer.TenderNumber),
                CsvField(offer.Title),
                CsvField(offer.Status),
                offer.TotalPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                CsvField(offer.Currency),
                offer.DeliveryDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                offer.ValidityDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CsvField(offer.PaymentTerms),
                CsvField(offer.TechnicalSummary),
                CsvField(offer.SubmittedAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture))
            }));
        }

        var csv = string.Join(Environment.NewLine, rows);
        var content = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv))
            .ToArray();

        await _audit.LogAsync("Exported", "ApplicationUser", userId,
            $"Personal offer summary exported as CSV; {offers.Count} offers.");
        return File(content, "text/csv; charset=utf-8", "my-offers-summary.csv");
    }

    private static string CsvField(string? value)
    {
        var safeValue = value?.Replace("\r", " ").Replace("\n", " ") ?? string.Empty;
        if (safeValue.Length > 0 && "=+-@".Contains(safeValue[0]))
            safeValue = $"'{safeValue}";

        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }

    #endregion
}
