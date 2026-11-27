using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Clarifications;

namespace TenderSystem.Areas.Shared.Controllers;

[Area("Shared")]
[Authorize]
public class ClarificationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ClarificationService _clarifications;

    public ClarificationsController(ApplicationDbContext db, ClarificationService clarifications)
    {
        _db = db;
        _clarifications = clarifications;
    }

    public async Task<IActionResult> Index(string? filter = "all", CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();

        var normalizedFilter = filter?.Trim().ToLowerInvariant() ?? "all";
        var isValidFilter = normalizedFilter == "all"
            || normalizedFilter == "received"
            || normalizedFilter == "sent"
            || normalizedFilter == "open"
            || normalizedFilter == "resolved";
        if (!isValidFilter)
            normalizedFilter = "all";

        var query = _db.Clarifications.AsNoTracking()
            .Where(item => item.SenderUserId == userId || item.ReceiverUserId == userId);
        var totalCount = await query.CountAsync(cancellationToken);
        var waitingForMeCount = await query.CountAsync(item =>
            item.Status != Statuses.ClarificationStatus.Resolved
            && item.ClarificationMessages.OrderByDescending(message => message.SentAt)
                .Select(message => message.SenderUserId).FirstOrDefault() != userId, cancellationToken);
        var waitingForReplyCount = await query.CountAsync(item =>
            item.Status != Statuses.ClarificationStatus.Resolved
            && item.ClarificationMessages.OrderByDescending(message => message.SentAt)
                .Select(message => message.SenderUserId).FirstOrDefault() == userId, cancellationToken);
        var resolvedCount = await query.CountAsync(item => item.Status == Statuses.ClarificationStatus.Resolved,
            cancellationToken);

        if (normalizedFilter == "received")
            query = query.Where(item => item.ReceiverUserId == userId);
        else if (normalizedFilter == "sent")
            query = query.Where(item => item.SenderUserId == userId);
        else if (normalizedFilter == "open")
            query = query.Where(item => item.Status != Statuses.ClarificationStatus.Resolved);
        else if (normalizedFilter == "resolved")
            query = query.Where(item => item.Status == Statuses.ClarificationStatus.Resolved);

        var items = await query
            .Include(item => item.SenderUser)
            .Include(item => item.ReceiverUser)
            .Include(item => item.Requisition)
            .Include(item => item.Tender)
            .Include(item => item.Bid).ThenInclude(bid => bid!.SupplierProfile)
            .Include(item => item.ClarificationMessages)
            .OrderByDescending(item => item.ClarificationMessages
                .Select(message => (DateTime?)message.SentAt).Max() ?? item.CreatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var model = new ClarificationsPageViewModel
        {
            Filter = normalizedFilter,
            TotalCount = totalCount,
            WaitingForMeCount = waitingForMeCount,
            WaitingForReplyCount = waitingForReplyCount,
            ResolvedCount = resolvedCount,
            Threads = items.Select(item => ToThreadRow(item, userId)).ToList()
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();
        var model = new ClarificationCreateViewModel
        {
            Routes = await _clarifications.GetCreateRoutesAsync(userId, cancellationToken)
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClarificationCreateViewModel model, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();
        model.Routes = await _clarifications.GetCreateRoutesAsync(userId, cancellationToken);
        if (!ModelState.IsValid) return View(model);

        var result = await _clarifications.CreateAsync(userId, model.RouteKey,
            model.Subject, model.Description, cancellationToken);
        if (result.Id is null)
        {
            ModelState.AddModelError(nameof(model.RouteKey), "That recipient or process is no longer available. Choose an available route and try again.");
            return View(model);
        }

        SetToast("Clarification sent", result.NotificationSent
            ? "Your question was sent to the selected person."
            : "Your question was saved. The recipient notification could not be delivered.",
            result.NotificationSent ? "success" : "warning");
        return RedirectToAction(nameof(Details), new { id = result.Id.Value });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();
        var item = await LoadThreadAsync(id, userId, cancellationToken);
        if (item is null) return NotFound();

        var permissions = await _clarifications.GetInteractionPermissionsAsync(id, userId, cancellationToken);

        var orderedMessages = item.ClarificationMessages
            .OrderBy(message => message.Id)
            .ToList();
        if (orderedMessages.Count > 0)
            orderedMessages.RemoveAt(0);

        var model = new ClarificationDetailsViewModel
        {
            Id = item.Id,
            Subject = item.Subject,
            Description = item.Description,
            Status = item.Status,
            Context = ContextLabel(item),
            OtherParty = item.SenderUserId == userId ? DisplayName(item.ReceiverUser) : DisplayName(item.SenderUser),
            CreatedAt = item.CreatedAt,
            CanReply = permissions.CanReply,
            CanResolve = permissions.CanResolve,
            ReplyAccessUnavailable = item.Status != Statuses.ClarificationStatus.Resolved && !permissions.CanReply,
            ReplyDraft = TempData["ClarificationReplyDraft"] as string ?? string.Empty,
            ReplyError = TempData["ClarificationReplyError"] as string,
            Messages = orderedMessages.Select(message => new ClarificationDetailsViewModel.ClarificationMessageItem(
                    DisplayName(message.SenderUser), message.Body, message.SentAt, message.SenderUserId == userId))
                .ToList()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(ClarificationReplyViewModel model, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["ClarificationReplyDraft"] = model.Body ?? string.Empty;
            TempData["ClarificationReplyError"] = "Write a message of up to 2,000 characters.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await _clarifications.ReplyAsync(model.Id, userId, model.Body, cancellationToken);
        if (result.Succeeded && result.NotificationSent)
            SetToast("Reply sent", "Your message was added to the conversation.", "success");
        else if (result.Succeeded)
            SetToast("Reply sent", "Your message was saved, but the recipient notification could not be delivered.", "warning");
        else
            SetToast("Reply not sent", "This conversation is unavailable or has already been resolved.", "danger");
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(int id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();
        var result = await _clarifications.ResolveAsync(id, userId, cancellationToken);
        if (result.Succeeded && result.NotificationSent)
            SetToast("Conversation resolved", "This clarification is now marked as resolved.", "success");
        else if (result.Succeeded)
            SetToast("Conversation resolved", "The clarification was resolved, but the other person could not be notified.", "warning");
        else
            SetToast("Unable to resolve", "You are not allowed to resolve this conversation.", "danger");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<Clarification?> LoadThreadAsync(int id, string userId, CancellationToken cancellationToken) =>
        await _db.Clarifications
            .Where(item => item.Id == id && (item.SenderUserId == userId || item.ReceiverUserId == userId))
            .Include(item => item.SenderUser)
            .Include(item => item.ReceiverUser)
            .Include(item => item.Requisition)
            .Include(item => item.Tender)
            .Include(item => item.Bid).ThenInclude(bid => bid!.SupplierProfile)
            .Include(item => item.ClarificationMessages).ThenInclude(message => message.SenderUser)
            .SingleOrDefaultAsync(cancellationToken);

    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static ClarificationsPageViewModel.ClarificationThreadRow ToThreadRow(Clarification item, string userId)
    {
        var lastMessage = item.ClarificationMessages.OrderByDescending(message => message.SentAt).FirstOrDefault();
        var preview = lastMessage?.Body ?? item.Description;
        return new ClarificationsPageViewModel.ClarificationThreadRow(
            item.Id,
            item.Subject,
            item.Status,
            ContextLabel(item),
            item.SenderUserId == userId ? DisplayName(item.ReceiverUser) : DisplayName(item.SenderUser),
            preview.Length > 135 ? preview[..132] + "…" : preview,
            lastMessage?.SentAt ?? item.CreatedAt,
            item.ReceiverUserId == userId);
    }

    private static string ContextLabel(Clarification item)
    {
        if (item.Bid is not null)
            return $"Tender {item.Bid.Tender?.TenderNumber ?? item.Tender?.TenderNumber ?? "—"} · Bid #{item.Bid.Id} · {item.Bid.SupplierProfile?.CompanyName ?? "Supplier"}";
        if (item.Tender is not null) return $"Tender {item.Tender.TenderNumber} · {item.Tender.Title}";
        if (item.Requisition is not null) return $"Request {item.Requisition.RequestNumber} · {item.Requisition.Title}";
        return "General process clarification";
    }

    private static string DisplayName(ApplicationUser? user) =>
        string.IsNullOrWhiteSpace(user?.FullName) ? "Team member" : user.FullName;

    private void SetToast(string title, string message, string type)
    {
        TempData["ToastTitle"] = title;
        TempData["ToastMessage"] = message;
        TempData["ToastType"] = type;
    }
}
