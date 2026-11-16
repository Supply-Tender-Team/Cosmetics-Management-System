using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Auth;
using TenderSystem.Services.Biometrics;
using TenderSystem.Services.Email;
using TenderSystem.Services.Notifications;

namespace TenderSystem.Services.Ai;

/// <summary>
/// AI-04, the assistant's command center. An Admin tells the assistant to DO something (send an email or a notification to
/// employees) and four things always happen in this order:
///
///   1. PROPOSE   the model only fills a fixed form; THIS CLASS decides the real recipients with a query and stores the action.
///   2. PREVIEW   the chat shows what will be sent and to whom.
///   3. APPROVE   the Admin proves it is really them with a fresh FINGERPRINT or FACE check, for this one action.
///   4. EXECUTE   once, from the stored action only, and everything is audited (never the message text).
///
/// The model never sends anything and never sees recipients or database data. See Services/Ai/README.md.
/// </summary>
public sealed partial class AiActionService
{
    private const int MaxIdentityFailures = 3;
    private const int MaxTries = 10;

    // adminId -> times (per minute proposals, per hour executions). The service lives one request, so these are static.
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Proposals = new();
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Executions = new();

    private readonly AiActionOptions _options;
    private readonly AiChatOptions _chat;
    private readonly IEnumerable<IAiChatProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly ApplicationDbContext _db;
    private readonly PasskeyService _passkeys;
    private readonly FaceService _face;
    private readonly EmailService _email;
    private readonly NotificationService _notify;
    private readonly AuditService _audit;
    private readonly PasswordService _passwords;
    private readonly ILogger<AiActionService> _log;

    public AiActionService(IOptions<AiActionOptions> options, IOptions<AiChatOptions> chat, IEnumerable<IAiChatProvider> providers,
        IMemoryCache cache, ApplicationDbContext db, PasskeyService passkeys, FaceService face, EmailService email,
        NotificationService notify, AuditService audit, PasswordService passwords, ILogger<AiActionService> log)
    {
        _options = options.Value;
        _chat = chat.Value;
        _providers = providers;
        _cache = cache;
        _db = db;
        _passkeys = passkeys;
        _face = face;
        _email = email;
        _notify = notify;
        _audit = audit;
        _passwords = passwords;
        _log = log;
    }

    public bool Enabled => _options.Enabled;

    private static string Key(string id) => "ai-action:" + id;

    // =================================================================== 1. PROPOSE

    public async Task<AiActionResult> ProposeAsync(ClaimsPrincipal user, string adminId, string adminName, string? request, CancellationToken ct)
    {
        if (!Enabled) return Fail("The command center is turned off.");
        if (!user.IsInRole(Roles.Admin)) return Fail("Only an Admin can give the assistant commands.");
        if (_chat.Provider is not ("ollama" or "openai")) return Fail("The AI assistant is turned off.");

        request = request?.Trim();
        if (string.IsNullOrEmpty(request)) return Fail("Please tell me what to send and to whom.");
        if (request.Length > 1000) return Fail("Please keep the request under 1000 characters.");

        // The Admin must be able to approve at all, so find out now instead of after the work is done.
        var canFingerprint = await _passkeys.CountAsync(adminId) > 0;
        var canFace = await _face.CountAsync(adminId) > 0;
        if (!canFingerprint && !canFace)
            return Fail("To approve an action you need a fingerprint or a face saved. Set one up in Profile & security first.");

        if (!TryTake(Proposals, adminId, _options.MaxProposalsPerMinute, TimeSpan.FromMinutes(1)))
            return Fail("You are giving commands too fast. Please wait a moment.");

        // ---- the model fills the form
        var provider = _providers.First(p => (_chat.Provider == "ollama") == (p is OllamaChatProvider));
        var messages = new List<AiChatMessage> { new("system", FormPrompt), new("user", request) };

        Form? form;
        try
        {
            form = await AskAsync(provider, messages, ct);
        }
        catch (AiUnavailableException ex)
        {
            return Fail(ex.Message);
        }
        if (form is null) return Fail("I could not understand that as a message to send. Please say what to send and to whom, for example: email everyone in Packaging that the meeting moved to 10:00.");
        if (form.Action == "none")
            return Fail("I did not find a message to send. I can send an email or a notification to employees: everyone, one role, one department, one person, or just you as a test. "
                + "Tell me what to send and to whom, for example: \"Email wadea@company.com: the meeting moved to 10:00.\"");

        // ---- the SERVER decides who gets it
        var audience = await ResolveAudienceAsync(form.AudienceKind, form.Role, form.Department, form.Person, adminId, ct);
        if (audience.Error != null) return Fail(audience.Error);

        var type = form.Action == "notify" ? AiActionType.Notify : AiActionType.Email;
        var subject = OneLine(form.Subject, 150);
        var body = BodyText(form.Body, type == AiActionType.Notify ? 500 : 2000);
        if (subject.Length == 0 || body.Length == 0) return Fail("The message needs a subject and some text. Please say what it should say.");

        var pending = new PendingAiAction
        {
            Id = Guid.NewGuid().ToString("N"),
            AdminId = adminId,
            AdminName = adminName,
            Type = type,
            AudienceLabel = audience.Label,
            Subject = subject,
            Body = body,
            Recipients = audience.Recipients,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(_options.TtlMinutes),
        };
        _cache.Set(Key(pending.Id), pending, TimeSpan.FromMinutes(_options.TtlMinutes));

        await _audit.LogAsync("AiActionProposed", "AiAction", pending.Id,
            $"{type} to {audience.Label}: {audience.Recipients.Count} recipient(s)");   // never the text of the message

        return new AiActionResult(true, "Please check this and approve it with your fingerprint or face.", ToCard(pending, canFingerprint, canFace, EmailNote(type)));
    }

    private const string FormPrompt = """
        You turn an Admin's request into ONE action by filling in a fixed form. You do not send anything: the Admin will review the form and must approve it.

        Allowed actions: "email" (send an email) or "notify" (send an in-app notification).
        Allowed audiences (field audience.kind):
        - "all_employees": every employee
        - "role": employees with one role; put the role in audience.role (Admin, Requester, SourcingOfficer, EvaluationCommittee, SourcingManager, TopCommittee)
        - "department": employees of one department; put its name in audience.department
        - "person": ONE employee; put their email address (or full name) in audience.person
        - "me": only the Admin, as a test
        Suppliers and anything else are NOT allowed. If the request is not about sending an email or notification to one of these audiences, set action to "none".
        If the Admin gives an email address or a person's name together with some text, they want that text sent to that person: use "email" (unless they say notification) and audience "person".

        Write "subject" (one short line) and "body" (plain text, polite and clear, at most 120 words, in the same language as the request).
        Use only what the Admin wrote. Never invent facts, dates, links or names, and do not use placeholders like [name].

        Reply with ONE JSON object and nothing else, exactly in this shape:
        {"action": "email" or "notify" or "none", "audience": {"kind": "all_employees" or "role" or "department" or "person" or "me", "role": string or null, "department": string or null, "person": string or null}, "subject": string, "body": string}
        """;

    private sealed record Form(string Action, string AudienceKind, string? Role, string? Department, string? Person, string Subject, string Body);

    private static async Task<Form?> AskAsync(IAiChatProvider provider, List<AiChatMessage> messages, CancellationToken ct)
    {
        var raw = await provider.CompleteAsync(messages, ct, json: true);
        var form = ParseForm(raw);
        if (form is not null) return form;

        var retry = new List<AiChatMessage>(messages)
        {
            new("assistant", raw),
            new("user", "That was not valid. Reply again with ONLY the JSON object in the exact shape given."),
        };
        return ParseForm(await provider.CompleteAsync(retry, ct, json: true));
    }

    /// <summary>Strict on purpose: anything outside the whitelist is rejected, never guessed.</summary>
    private static Form? ParseForm(string raw)
    {
        var text = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.IgnoreCase).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var action = Str(root, "action").ToLowerInvariant();
            if (action is not ("email" or "notify" or "none")) return null;
            if (action == "none") return new Form("none", "", null, null, null, "", "");

            if (!root.TryGetProperty("audience", out var aud) || aud.ValueKind != JsonValueKind.Object) return null;
            var kind = Str(aud, "kind").ToLowerInvariant();
            if (kind is not ("all_employees" or "role" or "department" or "person" or "me")) return null;

            return new Form(action, kind, NullIfEmpty(Str(aud, "role")), NullIfEmpty(Str(aud, "department")), NullIfEmpty(Str(aud, "person")),
                Str(root, "subject"), Str(root, "body"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    // ---- who gets it: a query, never the model

    private sealed record Audience(string Label, List<AiActionRecipient> Recipients, string? Error);

    private async Task<Audience> ResolveAudienceAsync(string kind, string? roleText, string? departmentText, string? personText, string adminId, CancellationToken ct)
    {
        // Employees only: active, with an email, never a supplier (by user type OR by role).
        var supplierRoleId = await _db.Roles.Where(r => r.Name == Roles.Supplier).Select(r => r.Id).FirstOrDefaultAsync(ct);
        IQueryable<ApplicationUser> query = _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.UserType != "Supplier"
                        && !_db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == supplierRoleId));

        string label;
        switch (kind)
        {
            case "all_employees":
                label = "all employees";
                break;

            case "me":
                query = query.Where(u => u.Id == adminId);
                label = "you only (a test)";
                break;

            case "role":
            {
                var role = Roles.All.Where(r => r != Roles.Supplier).FirstOrDefault(r => SameName(r, roleText) || SameName(Roles.LabelFor(r), roleText));
                if (role is null) return new Audience("", [], "I do not know that role. Roles: " + string.Join(", ", Roles.All.Where(r => r != Roles.Supplier).Select(Roles.LabelFor)) + ".");
                var roleId = await _db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync(ct);
                query = query.Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
                label = "everyone with the role " + Roles.LabelFor(role);
                break;
            }

            case "person":
            {
                // ONE existing employee, found by email or by full name. Anyone without an account here (or a supplier) is not reachable.
                var wanted = (personText ?? "").Trim().ToLowerInvariant();
                if (wanted.Length < 3) return new Audience("", [], "Please tell me which employee: their email address or full name.");
                var found = await query.Where(u => u.Email!.ToLower() == wanted || u.FullName.ToLower() == wanted)
                    .Select(u => new { u.Id, u.FullName, u.Email }).Take(2).ToListAsync(ct);
                if (found.Count == 0)
                    return new Audience("", [], "No active employee has that email address or name. I can only message people who have an account in this system. "
                        + "(To message someone new, add them under Users first.)");
                if (found.Count > 1)
                    return new Audience("", [], "That name matches more than one employee. Please use the email address.");
                query = query.Where(u => u.Id == found[0].Id);
                label = "one person: " + (string.IsNullOrWhiteSpace(found[0].FullName) ? found[0].Email : found[0].FullName);
                break;
            }

            case "department":
            {
                var departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.Id, d.Name, d.Code }).ToListAsync(ct);
                var matches = departments.Where(d => SameName(d.Name, departmentText) || SameName(d.Code, departmentText)).ToList();
                if (matches.Count == 0) matches = departments.Where(d => departmentText is { Length: > 2 } t && d.Name.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count != 1)
                    return new Audience("", [], (matches.Count == 0 ? "I could not find that department." : "That matches more than one department.")
                        + " Departments: " + string.Join(", ", departments.Select(d => d.Name)) + ".");
                var departmentId = matches[0].Id;
                query = query.Where(u => u.DepartmentId == departmentId);
                label = "the department " + matches[0].Name;
                break;
            }

            default:
                return new Audience("", [], "That audience is not allowed.");
        }

        var rows = await query.OrderBy(u => u.FullName).Take(_options.MaxRecipients + 1)
            .Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        if (rows.Count > _options.MaxRecipients)
            return new Audience("", [], $"That would reach more than {_options.MaxRecipients} people. Please narrow the audience, for example one department or one role.");
        if (rows.Count == 0)
            return new Audience("", [], "No active employees match that audience.");

        return new Audience(label, rows.Select(r => new AiActionRecipient(r.Id, string.IsNullOrWhiteSpace(r.FullName) ? r.Email! : r.FullName, r.Email!)).ToList(), null);
    }

    private static bool SameName(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

    // ---- text hygiene

    /// <summary>One line only (a newline in a subject could inject email headers), cut to length.</summary>
    private static string OneLine(string? s, int max)
    {
        var t = Regex.Replace(s ?? "", @"[\r\n\t\u0000-\u001F]+", " ").Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }

    private static string BodyText(string? s, int max)
    {
        var t = Regex.Replace((s ?? "").Replace("\r", ""), @"[\u0000-\u0008\u000B\u000C\u000E-\u001F]", "");
        t = Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }

    /// <summary>When emails are not really delivered (SMTP off), say so before the Admin approves, so nobody thinks a person was emailed.</summary>
    private string? EmailNote(AiActionType type) =>
        type == AiActionType.Email && _email.Mode != "smtp"
            ? "Email sending is not connected (SMTP is off). Approving will only save the message as a file on the server. Nothing will really be sent."
            : null;

    private static AiActionCard ToCard(PendingAiAction a, bool canFingerprint, bool canFace, string? note = null) => new(
        a.Id, a.Type == AiActionType.Email ? "email" : "notify",
        a.Type == AiActionType.Email ? $"Send an email to {a.Recipients.Count} {(a.Recipients.Count == 1 ? "person" : "people")}" : $"Send a notification to {a.Recipients.Count} {(a.Recipients.Count == 1 ? "person" : "people")}",
        a.AudienceLabel, a.Recipients.Count, a.Recipients.Take(10).Select(r => r.Name).ToList(), a.Subject, a.Body, a.ExpiresUtc, canFingerprint, canFace, note);

    // =================================================================== 2. find / cancel

    /// <summary>The Admin's own proposed action that is still waiting, or null. Expiry is handled here.</summary>
    public PendingAiAction? FindOwned(string adminId, string actionId)
    {
        if (string.IsNullOrEmpty(actionId) || !_cache.TryGetValue(Key(actionId), out PendingAiAction? a) || a is null) return null;
        if (a.AdminId != adminId) return null;                      // someone else's action looks the same as a missing one
        lock (a)
        {
            if (a.State == AiActionState.Proposed && a.ExpiresUtc <= DateTime.UtcNow) a.State = AiActionState.Expired;
            return a.State == AiActionState.Proposed ? a : null;
        }
    }

    public async Task<AiActionResult> CancelAsync(string adminId, string actionId)
    {
        var a = FindOwned(adminId, actionId);
        if (a is null) return Fail("That action is no longer waiting.");
        lock (a) { if (a.State != AiActionState.Proposed) return Fail("That action is no longer waiting."); a.State = AiActionState.Cancelled; }
        await _audit.LogAsync("AiActionCancelled", "AiAction", a.Id, "Cancelled by the Admin before approval");
        return new AiActionResult(true, "Cancelled. Nothing was sent.", State: "Cancelled");
    }

    // =================================================================== 3. APPROVE (fingerprint or face)

    public async Task<AiActionResult> ApproveWithPasskeyAsync(ApplicationUser admin, string actionId, PasskeyLoginRequest request, CancellationToken ct)
    {
        var a = FindOwned(admin.Id, actionId);
        if (a is null) return Fail("That action is no longer waiting. Please ask again.");

        var result = await _passkeys.CompleteApprovalAsync(admin, a.Id, request, ct);
        if (!result.Ok) return await FailedAttemptAsync(a, result.Message, identityFailure: result.UserId != null);
        return await ExecuteAsync(a, admin, "fingerprint");
    }

    /// <param name="strict">true right after "not sure": the browser asks again WITH a head turn</param>
    public FaceChallenge? BeginFace(string adminId, string actionId, bool strict = false)
    {
        var a = FindOwned(adminId, actionId);
        return a is null ? null : _face.CreateChallenge("approve", FaceSubject(adminId, a.Id), strict);
    }

    public async Task<AiActionResult> ApproveWithFaceAsync(ApplicationUser admin, string actionId, FaceSubmitRequest request)
    {
        var a = FindOwned(admin.Id, actionId);
        if (a is null) return Fail("That action is no longer waiting. Please ask again.");

        var analysis = await _face.AnalyzeAsync(request.ChallengeId, "approve", FaceSubject(admin.Id, a.Id), request.Frames);
        if (!analysis.Ok)
        {
            var failed = await FailedAttemptAsync(a, analysis.Reason, identityFailure: false);
            // "not sure it is a live face" is not a wrong face: the browser asks again once WITH a head turn (it still counts as a try)
            return analysis.NeedsTurn && failed.State != "Cancelled" ? failed with { State = "NeedTurn" } : failed;
        }

        var match = await _face.MatchAsync(admin.Id, analysis.Embedding!);       // 1:1 against the signed-in Admin's own face only
        if (!match.Ok) return await FailedAttemptAsync(a, "That face does not match your saved face.", identityFailure: true);

        return await ExecuteAsync(a, admin, "face");
    }

    private static string FaceSubject(string adminId, string actionId) => adminId + "|" + actionId;

    /// <summary>Every wrong proof counts. Too many cancel the action, so it cannot be guessed at.</summary>
    private async Task<AiActionResult> FailedAttemptAsync(PendingAiAction a, string message, bool identityFailure)
    {
        var cancelled = false;
        lock (a)
        {
            a.Tries++;
            if (identityFailure) a.IdentityFailures++;
            if (a.State == AiActionState.Proposed && (a.IdentityFailures >= MaxIdentityFailures || a.Tries >= MaxTries))
            {
                a.State = AiActionState.Cancelled;
                cancelled = true;
            }
        }

        if (cancelled)
        {
            await _audit.LogAsync("AiActionApprovalFailed", "AiAction", a.Id, "Too many failed fingerprint or face checks, the action was cancelled");
            return new AiActionResult(false, message + " The action was cancelled after too many failed attempts.", State: "Cancelled");
        }
        return Fail(message);
    }

    // =================================================================== 4. EXECUTE (once)

    private async Task<AiActionResult> ExecuteAsync(PendingAiAction a, ApplicationUser admin, string method)
    {
        lock (a)
        {
            if (a.State != AiActionState.Proposed) return Fail("That action was already used.");
            if (a.ExpiresUtc <= DateTime.UtcNow) { a.State = AiActionState.Expired; return Fail("That action expired. Please ask again."); }
            if (a.Type == AiActionType.CreateAccounts && !TryTakeMany(AccountExecutions, admin.Id, a.AccountRows.Count, _options.MaxAccountsPerHour, TimeSpan.FromHours(1)))
                return Fail($"You have reached the limit of {_options.MaxAccountsPerHour} accounts created per hour. Please try again later.");
            if (!TryTake(Executions, admin.Id, _options.MaxPerHour, TimeSpan.FromHours(1)))
                return Fail($"You have reached the limit of {_options.MaxPerHour} actions per hour. Please try again later.");
            a.State = AiActionState.Approved;           // from here the action can never be started again
            a.ApprovedWith = method;
        }

        await _audit.LogAsync("AiActionApproved", "AiAction", a.Id, $"Approved with {method}");
        if (a.Type is AiActionType.CreateAccounts or AiActionType.BulkMessage) return await ExecuteFileAsync(a, admin);   // AI-09: Excel file actions

        // The Admin approved this exact action, so it is finished even if the browser goes away (no cancellation token here).
        int sent = 0, failed = 0;
        foreach (var r in a.Recipients)
        {
            try
            {
                if (a.Type == AiActionType.Email)
                {
                    var ok = await _email.SendAsync(r.Email, a.Subject, EmailTemplates.Announcement(a.Subject, a.Body, a.AdminName), CancellationToken.None);
                    if (ok) sent++; else failed++;
                }
                else
                {
                    await _notify.NotifyAsync(r.UserId, "AdminAnnouncement", a.Subject, a.Body);
                    sent++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _log.LogWarning(ex, "AI action {Id}: could not reach one recipient", a.Id);
            }
        }

        lock (a) { a.State = sent == 0 ? AiActionState.Failed : AiActionState.Done; }
        await _audit.LogAsync("AiActionExecuted", "AiAction", a.Id,
            $"{a.Type} to {a.AudienceLabel}: sent {sent}, failed {failed}. Subject: {Cut(a.Subject, 80)}");

        var what = a.Type == AiActionType.Email ? "email" : "notification";
        var message = failed == 0 && EmailNote(a.Type) != null
                ? $"Done, but email sending is not connected (SMTP is off): the {what} was saved as a file in App_Data/emails for {sent} {(sent == 1 ? "person" : "people")} and was NOT really sent."
            : failed == 0 ? $"Done. The {what} was sent to {sent} {(sent == 1 ? "person" : "people")}."
            : sent == 0 ? $"The {what} could not be sent to anyone. Please check the email settings."
            : $"The {what} reached {sent} of {sent + failed} people. {failed} could not be reached.";
        return new AiActionResult(sent > 0, message, Sent: sent, Failed: failed, State: sent == 0 ? "Failed" : "Done");
    }

    // =================================================================== helpers

    private static AiActionResult Fail(string message) => new(false, message);

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Allows max takes inside the window; takes one slot when it returns true.</summary>
    private static bool TryTake(ConcurrentDictionary<string, Queue<DateTime>> store, string key, int max, TimeSpan window)
    {
        var queue = store.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            var now = DateTime.UtcNow;
            while (queue.Count > 0 && now - queue.Peek() > window) queue.Dequeue();
            if (queue.Count >= max) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
