using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Sourcing.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Files;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Areas.Sourcing.Controllers;

[Area("Sourcing")]
[Authorize(Roles = Roles.SourcingOfficer)]
public class ComplianceController : Controller
{
    private static readonly string[] ReviewableBidStatuses =
    [
        Statuses.BidStatus.Submitted,
        Statuses.BidStatus.UnderReview,
        Statuses.BidStatus.Compliant,
        Statuses.BidStatus.NonCompliant
    ];

    private static readonly string[] ComplianceResults =
    [
        Statuses.ComplianceResult.Compliant,
        Statuses.ComplianceResult.NonCompliant,
        Statuses.ComplianceResult.PendingClarification
    ];

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly IFileStorageService _fileStorage;
    private readonly IWebHostEnvironment _environment;
    private readonly NotificationService _notifications;

    public ComplianceController(
        ApplicationDbContext db,
        AuditService audit,
        IFileStorageService fileStorage,
        IWebHostEnvironment environment,
        NotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
        _environment = environment;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? tenderId, int? bidId, CancellationToken cancellationToken)
    {
        var model = await BuildPageAsync(tenderId, bidId, null, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> SupplierDocument(
        int tenderId, int bidId, int documentId, bool inline, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var supplierId = await _db.Bids.AsNoTracking()
            .Where(bid => bid.Id == bidId && bid.TenderId == tenderId
                && bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status))
            .Select(bid => bid.SupplierProfileId)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplierId == 0) return NotFound();

        var document = await _db.SupplierDocuments.AsNoTracking()
            .Where(row => row.Id == documentId && row.SupplierProfileId == supplierId)
            .Select(row => new { row.DocType, row.FilePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null) return NotFound();

        var extension = Path.GetExtension(document.FilePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
        if (inline && contentType is not ("application/pdf" or "image/png" or "image/jpeg"))
            return BadRequest();

        Stream? stream;
        try
        {
            stream = await _fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        if (stream is null) return NotFound();

        await using (stream)
        {
            var accessedAt = DateTime.UtcNow;
            var history = await DocumentAccessLogService.RecordAndGetHistoryAsync(
                _db, userId, "SupplierDocument", documentId,
                inline ? Statuses.DocumentAction.View : Statuses.DocumentAction.Download,
                accessedAt, cancellationToken);
            var fileName = document.DocType.Replace(' ', '-') + extension;
            byte[] content;
            if (contentType == "application/pdf")
            {
                var viewerName = history.FirstOrDefault()?.ViewerName ?? "Signed-in user";
                content = await DocumentWatermarkService.CreateWatermarkedCopyAsync(
                    stream, fileName, viewerName, accessedAt, _environment.WebRootPath,
                    cancellationToken, history);
            }
            else
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.XContentTypeOptions = "nosniff";
            return inline ? File(content, contentType) : File(content, contentType, fileName);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [Bind(Prefix = "Decision")] ComplianceDecisionInputViewModel input,
        CancellationToken cancellationToken)
    {
        if (!ComplianceResults.Contains(input.Result, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Decision.Result", "Choose one of the available compliance decisions.");
        }

        var bidIsReviewable = await IsReviewableBidAsync(input.TenderId, input.BidId, cancellationToken);
        if (!bidIsReviewable) return NotFound();

        if (!ModelState.IsValid)
        {
            var page = await BuildPageAsync(input.TenderId, input.BidId, input, cancellationToken);
            return page is null ? NotFound() : View(nameof(Index), page);
        }

        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(reviewerId)) return Challenge();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var bidStatus = input.Result == Statuses.ComplianceResult.PendingClarification
            ? Statuses.BidStatus.UnderReview : input.Result;
        var changed = await _db.Bids.Where(bid => bid.Id == input.BidId && bid.TenderId == input.TenderId
                && bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status))
            .ExecuteUpdateAsync(update => update.SetProperty(bid => bid.Status, bidStatus), cancellationToken);
        if (changed == 0) return NotFound();

        var check = new ComplianceCheck
        {
            BidId = input.BidId,
            CheckedByUserId = reviewerId,
            Result = input.Result,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
            CheckedAt = DateTime.UtcNow
        };

        _db.ComplianceChecks.Add(check);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Checked",
            "ComplianceCheck",
            check.Id.ToString(),
            $"{check.Result} review saved for Bid {check.BidId}.");

        await transaction.CommitAsync(cancellationToken);

        TempData["ToastTitle"] = "Compliance decision saved";
        TempData["ToastMessage"] = "The review was saved successfully.";
        TempData["ToastType"] = "success";

        // Return to the tender overview after saving so the decision form closes.
        return RedirectToAction(nameof(Index), new { tenderId = input.TenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToCommittee(
        int id, CancellationToken cancellationToken, bool skipChecks = false)
    {
        var destination = RedirectToAction(nameof(Index), new { tenderId = id });
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var tender = await _db.Tenders.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Id, row.TenderNumber, row.Title, row.Status, row.SubmissionDeadline })
            .SingleOrDefaultAsync(cancellationToken);
        if (tender is null) return NotFound();
        if (skipChecks && !_environment.IsDevelopment()) return Forbid();

        var allowedStatuses = new[]
        {
            Statuses.TenderStatus.Published,
            Statuses.TenderStatus.Open,
            Statuses.TenderStatus.Closed
        };
        if (!allowedStatuses.Contains(tender.Status, StringComparer.Ordinal))
        {
            TempData["ToastTitle"] = "Evaluation not sent";
            TempData["ToastMessage"] = "Only a published tender with submitted offers can be sent for evaluation.";
            TempData["ToastType"] = "warning";
            return destination;
        }

        if (!skipChecks && tender.SubmissionDeadline > DateTime.UtcNow)
        {
            TempData["ToastTitle"] = "Submission period is still open";
            TempData["ToastMessage"] = $"Offers can be sent for evaluation after {tender.SubmissionDeadline:dd MMM yyyy, HH:mm} UTC.";
            TempData["ToastType"] = "warning";
            return destination;
        }

        var compliantSupplierCount = await _db.Bids.AsNoTracking()
            .Where(bid => bid.TenderId == id && bid.SubmittedAt.HasValue
                && bid.Status == Statuses.BidStatus.Compliant)
            .Select(bid => bid.SupplierProfileId)
            .Distinct()
            .CountAsync(cancellationToken);
        if (!skipChecks && compliantSupplierCount < 3)
        {
            TempData["ToastTitle"] = "Evaluation not sent";
            TempData["ToastMessage"] = $"At least 3 compliant suppliers are required. This tender currently has {compliantSupplierCount}.";
            TempData["ToastType"] = "warning";
            return destination;
        }

        var offersAwaitingReview = await _db.Bids.AsNoTracking()
            .AnyAsync(bid => bid.TenderId == id && bid.SubmittedAt.HasValue
                && (bid.Status == Statuses.BidStatus.Submitted || bid.Status == Statuses.BidStatus.UnderReview),
                cancellationToken);
        if (!skipChecks && offersAwaitingReview)
        {
            TempData["ToastTitle"] = "Compliance review is not complete";
            TempData["ToastMessage"] = "Finish the compliance review for all submitted offers before sending them to evaluation.";
            TempData["ToastType"] = "warning";
            return destination;
        }

        var committee = await _db.CommitteeMembers.AsNoTracking()
            .Where(member => member.TenderId == id)
            .Select(member => new
            {
                member.UserId,
                member.MemberRole,
                member.User.IsActive
            })
            .ToListAsync(cancellationToken);
        var committeeUserIds = committee.Select(member => member.UserId).Distinct().ToArray();
        var activeEvaluatorIds = await _db.UserRoles.AsNoTracking()
            .Join(_db.Roles.AsNoTracking(), userRole => userRole.RoleId, role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .Where(row => committeeUserIds.Contains(row.UserId) && row.Name == Roles.EvaluationCommittee)
            .Select(row => row.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var validCommittee = committee.Count == CommitteePositions.All.Length
            && committee.Select(member => member.UserId).Distinct().Count() == CommitteePositions.All.Length
            && CommitteePositions.All.All(seat => committee.Count(member => member.MemberRole == seat.Code) == 1)
            && committee.All(member => member.IsActive && activeEvaluatorIds.Contains(member.UserId));
        var notificationMembers = committee
            .Where(member => member.IsActive && activeEvaluatorIds.Contains(member.UserId))
            .ToList();
        if (skipChecks ? notificationMembers.Count == 0 : !validCommittee)
        {
            TempData["ToastTitle"] = skipChecks ? "No active test recipient" : "Evaluation committee is incomplete";
            TempData["ToastMessage"] = skipChecks
                ? "Assign at least one active Evaluation Committee member to this tender before running the test."
                : "Assign four active Evaluation Committee members to all committee positions before sending.";
            TempData["ToastType"] = "warning";
            return destination;
        }

        var link = $"/Sourcing/CommitteeBrief/Index/{id}";
        foreach (var member in notificationMembers)
        {
            var alreadyNotified = await _db.Notifications.AsNoTracking().AnyAsync(notification =>
                notification.UserId == member.UserId
                && notification.Type == "TenderEvaluation"
                && notification.Link == link,
                cancellationToken);
            if (!alreadyNotified)
            {
                await _notifications.NotifyAsync(
                    member.UserId,
                    "TenderEvaluation",
                    "Evaluation brief shared",
                    $"The tender and compliant offers for {tender.TenderNumber}: {tender.Title} are ready for evaluation.",
                    link);
            }
        }

        await _audit.LogAsync(
            "Evaluation shared",
            "Tender",
            id.ToString(),
            $"{compliantSupplierCount} compliant suppliers shared with the evaluation committee."
            + (skipChecks ? " Development test bypassed the deadline, supplier-count and compliance-completion checks." : string.Empty));
        await transaction.CommitAsync(cancellationToken);

        TempData["ToastTitle"] = skipChecks ? "Test evaluation sent" : "Evaluation sent";
        TempData["ToastMessage"] = skipChecks
            ? $"Test mode: workflow checks were bypassed. The evaluation was sent to {notificationMembers.Count} active committee member(s)."
            : $"The evaluation committee can now review {compliantSupplierCount} compliant supplier(s).";
        TempData["ToastType"] = "success";
        return destination;
    }

    private async Task<CompliancePageViewModel?> BuildPageAsync(
        int? tenderId,
        int? bidId,
        ComplianceDecisionInputViewModel? decision,
        CancellationToken cancellationToken)
    {
        var tenders = await _db.Tenders.AsNoTracking()
            .Where(tender => tender.Bids.Any(bid =>
                bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status)))
            .LatestActivityFirst(_db)
            .Select(tender => new CompliancePageViewModel.TenderOption(
                tender.Id, tender.TenderNumber, tender.Title, tender.Status, tender.CreatedAt, tender.SubmissionDeadline))
            .ToListAsync(cancellationToken);

        if (tenderId.HasValue && tenders.All(tender => tender.Id != tenderId.Value)) return null;
        if (bidId.HasValue && !tenderId.HasValue) return null;

        CompliancePageViewModel.TenderDetails? selectedTender = null;
        List<CompliancePageViewModel.BidRow> bids = [];
        CompliancePageViewModel.BidDetails? selectedBid = null;
        List<CompliancePageViewModel.ExtractedRequirement> requirements = [];

        if (tenderId.HasValue)
        {
            selectedTender = await _db.Tenders.AsNoTracking()
                .Where(tender => tender.Id == tenderId.Value
                    && tender.Bids.Any(bid => bid.SubmittedAt.HasValue && ReviewableBidStatuses.Contains(bid.Status)))
                .Select(tender => new CompliancePageViewModel.TenderDetails(
                    tender.Id, tender.RequisitionId, tender.TenderNumber, tender.Title, tender.Description, tender.Terms,
                    tender.Status, tender.SubmissionDeadline))
                .SingleOrDefaultAsync(cancellationToken);

            if (selectedTender is null) return null;

            requirements = ComplianceTermsExtractor.Extract(selectedTender.Terms);
            var requestedItems = await _db.PurchaseRequisitionItems.AsNoTracking()
                .Where(item => item.RequisitionId == selectedTender.RequisitionId)
                .OrderBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.ItemName,
                    item.Quantity,
                    item.Unit,
                    item.Specifications
                })
                .ToListAsync(cancellationToken);

            bids = await _db.Bids.AsNoTracking()
                .Where(bid => bid.TenderId == tenderId.Value
                    && bid.SubmittedAt.HasValue
                    && ReviewableBidStatuses.Contains(bid.Status))
                .OrderByActivity(_db)
                .Select(bid => new CompliancePageViewModel.BidRow(
                    bid.Id,
                    bid.SupplierProfileId,
                    bid.SupplierProfile.CompanyName,
                    bid.SubmittedAt!.Value,
                    bid.Status,
                    bid.ComplianceChecks
                        .OrderByDescending(check => check.CheckedAt)
                        .ThenByDescending(check => check.Id)
                        .Select(check => check.Result)
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);

            if (bidId.HasValue && bids.All(bid => bid.Id != bidId.Value)) return null;

            if (bidId.HasValue)
            {
                selectedBid = await _db.Bids.AsNoTracking()
                    .Where(bid => bid.Id == bidId.Value
                        && bid.TenderId == tenderId.Value
                        && bid.SubmittedAt.HasValue
                        && ReviewableBidStatuses.Contains(bid.Status))
                    .Select(bid => new CompliancePageViewModel.BidDetails(
                        bid.Id,
                        bid.TenderId,
                        bid.SupplierProfile.CompanyName,
                        bid.Status,
                        bid.SubmittedAt!.Value,
                        bid.SupplierProfileId,
                        bid.SupplierProfile.TaxNumber,
                        bid.SupplierProfile.CommercialRegNumber,
                        bid.SupplierProfile.Status,
                        bid.DeliveryDays,
                        bid.ValidityDays,
                        bid.PaymentTerms))
                    .SingleOrDefaultAsync(cancellationToken);

                if (selectedBid is null) return null;

                var offeredItems = await _db.BidItems.AsNoTracking()
                    .Where(item => item.BidId == bidId.Value)
                    .Select(item => new { item.RequisitionItemId, item.Quantity, item.OfferedSpecifications })
                    .ToListAsync(cancellationToken);

                var itemCoverage = requestedItems.Select(item =>
                {
                    var offered = offeredItems.FirstOrDefault(response => response.RequisitionItemId == item.Id);
                    var status = offered is null
                        ? "Missing from offer"
                        : offered.Quantity != item.Quantity
                            ? "Quantity differs"
                            : !string.IsNullOrWhiteSpace(item.Specifications)
                                ? string.IsNullOrWhiteSpace(offered.OfferedSpecifications)
                                    ? "Supplier specifications missing"
                                    : "Manual specification review"
                                : "Item and quantity match";

                    return new CompliancePageViewModel.ItemCoverageRow(
                        item.ItemName,
                        item.Quantity,
                        item.Unit,
                        item.Specifications,
                        offered?.Quantity,
                        offered?.OfferedSpecifications ?? string.Empty,
                        status);
                }).ToList();

                var documents = await _db.BidDocuments.AsNoTracking()
                    .Where(document => document.BidId == bidId.Value)
                    .OrderByDescending(document => document.UploadedAt)
                    .ThenByDescending(document => document.Id)
                    .Select(document => new CompliancePageViewModel.DocumentRow(
                        document.Id,
                        document.DocType,
                        document.FileName,
                        document.UploadedAt,
                        document.SizeBytes,
                        document.BidGuaranteeId))
                    .ToListAsync(cancellationToken);

                var supplierDocuments = await _db.SupplierDocuments.AsNoTracking()
                    .Where(document => document.SupplierProfileId == selectedBid.SupplierProfileId)
                    .OrderBy(document => document.DocType)
                    .ThenByDescending(document => document.UploadedAt)
                    .Select(document => new
                    {
                        document.Id,
                        document.DocType,
                        document.ExpiryDate,
                        document.UploadedAt,
                        document.FilePath
                    })
                    .ToListAsync(cancellationToken);

                var guarantees = await _db.BidGuarantees.AsNoTracking()
                    .Where(guarantee => guarantee.BidId == bidId.Value)
                    .OrderBy(guarantee => guarantee.Id)
                    .Select(guarantee => new
                    {
                        guarantee.Id,
                        guarantee.GuaranteeType,
                        guarantee.ReferenceNumber,
                        guarantee.Amount,
                        guarantee.Currency,
                        guarantee.ExpiresAt
                    })
                    .ToListAsync(cancellationToken);

                requirements = requirements.Select(requirement => requirement with
                {
                    RelatedDocuments = documents
                        .Where(document => ComplianceTermsExtractor.CouldRelateDocument(
                            requirement.Name, document.DocType, document.FileName))
                        .ToList()
                }).ToList();

                var reviews = await _db.ComplianceChecks.AsNoTracking()
                    .Where(check => check.BidId == bidId.Value)
                    .OrderByDescending(check => check.CheckedAt)
                    .ThenByDescending(check => check.Id)
                    .Select(check => new CompliancePageViewModel.ReviewRow(
                        check.Result,
                        check.CheckedByUser.FullName,
                        check.CheckedAt,
                        check.Reason))
                    .ToListAsync(cancellationToken);

                var today = DateTime.UtcNow.Date;
                selectedBid = selectedBid with
                {
                    ItemCoverage = itemCoverage,
                    SupplierDocuments = supplierDocuments.Select(document => new CompliancePageViewModel.SupplierDocumentRow(
                        document.Id,
                        document.DocType,
                        document.ExpiryDate,
                        document.UploadedAt,
                        Path.GetExtension(document.FilePath).ToLowerInvariant() is ".pdf" or ".png" or ".jpg" or ".jpeg",
                        document.ExpiryDate.HasValue && document.ExpiryDate.Value < today)).ToList(),
                    Guarantees = guarantees.Select(guarantee => new CompliancePageViewModel.GuaranteeRow(
                        guarantee.Id,
                        guarantee.GuaranteeType,
                        guarantee.ReferenceNumber,
                        guarantee.Amount,
                        guarantee.Currency,
                        guarantee.ExpiresAt,
                        guarantee.ExpiresAt.HasValue && guarantee.ExpiresAt.Value.Date < today,
                        documents.Where(document => document.BidGuaranteeId == guarantee.Id).ToList())).ToList(),
                    Reviews = reviews
                };
            }
        }

        return new CompliancePageViewModel
        {
            Tenders = tenders,
            Requirements = requirements,
            SelectedTenderId = tenderId,
            SelectedTender = selectedTender,
            Bids = bids,
            SelectedBid = selectedBid,
            Decision = decision ?? new ComplianceDecisionInputViewModel
            {
                TenderId = tenderId ?? 0,
                BidId = bidId ?? 0
            }
        };
    }

    private Task<bool> IsReviewableBidAsync(int tenderId, int bidId, CancellationToken cancellationToken) =>
        _db.Bids.AsNoTracking().AnyAsync(bid =>
            bid.Id == bidId
            && bid.TenderId == tenderId
            && bid.SubmittedAt.HasValue
            && ReviewableBidStatuses.Contains(bid.Status), cancellationToken);
}
