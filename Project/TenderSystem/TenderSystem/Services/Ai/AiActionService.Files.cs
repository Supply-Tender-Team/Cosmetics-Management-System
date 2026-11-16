using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Email;

namespace TenderSystem.Services.Ai;

/// <summary>
/// AI-09: ACTIONS FROM AN EXCEL FILE. The Admin downloads a template made by the server (create accounts, or send messages), fills it
/// and attaches it in the Command tab. The file is read and checked by CODE (<see cref="AiExcelTemplates"/> and the validators below):
/// the model never sees the rows. It only writes the optional COMMON text of a messages file ("tell them the system is down tomorrow").
/// Then the same four steps as every action: PROPOSE (preview with the valid and the skipped rows), APPROVE with a fresh fingerprint or
/// face for THIS file, EXECUTE once from the rows stored on the server, AUDIT (counts only, never names, emails or text).
/// </summary>
public sealed partial class AiActionService
{
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> AccountExecutions = new();

    // Keep in step with Areas/Admin/Controllers/UsersController.EmployeeRoles (another developer's file, not edited here).
    // Admin and Supplier are never created from a file.
    private static readonly string[] AccountRoles =
        [Roles.Requester, Roles.SourcingOfficer, Roles.EvaluationCommittee, Roles.SourcingManager, Roles.TopCommittee];

    private static readonly Regex EmailPattern = new(@"^[^@\s,;<>]+@[^@\s,;<>]+\.[^@\s,;<>]{2,}$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"^07[789]\d{7}$", RegexOptions.Compiled);

    // =================================================================== the template

    public async Task<(byte[]? File, string? Error)> CreateTemplateAsync(ClaimsPrincipal user, string kind, CancellationToken ct)
    {
        if (!Enabled) return (null, "The command center is turned off.");
        if (!user.IsInRole(Roles.Admin)) return (null, "Only an Admin can use the templates.");
        if (kind is not (AiExcelTemplates.AccountsKind or AiExcelTemplates.MessagesKind)) return (null, "Unknown template.");

        var departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).Select(d => d.Name).ToListAsync(ct);
        return (AiExcelTemplates.CreateTemplate(kind, AccountRoles, departments), null);
    }

    // =================================================================== 1. PROPOSE from a file

    public async Task<AiActionResult> ProposeFileAsync(ClaimsPrincipal user, string adminId, string adminName, string kind, bool notify,
        string? request, string fileName, byte[] file, CancellationToken ct)
    {
        if (!Enabled) return Fail("The command center is turned off.");
        if (!user.IsInRole(Roles.Admin)) return Fail("Only an Admin can give the assistant commands.");
        if (kind is not (AiExcelTemplates.AccountsKind or AiExcelTemplates.MessagesKind)) return Fail("Please choose a template: create accounts, or send messages.");
        if (file.Length == 0) return Fail("Please attach the Excel file made from the template.");
        if (!(fileName ?? "").EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return Fail("Please attach an Excel .xlsx file made from the template.");
        if (file.Length > _options.MaxFileMb * 1024L * 1024L) return Fail($"The file is bigger than {_options.MaxFileMb} MB. Please use only the template.");

        request = request?.Trim();
        if (request is { Length: > 1000 }) return Fail("Please keep the note under 1000 characters.");

        var canFingerprint = await _passkeys.CountAsync(adminId) > 0;
        var canFace = await _face.CountAsync(adminId) > 0;
        if (!canFingerprint && !canFace)
            return Fail("To approve an action you need a fingerprint or a face saved. Set one up in Profile & security first.");

        if (kind == AiExcelTemplates.AccountsKind && _email.Mode == "off")
            return Fail("Accounts cannot be created now: every new person gets a temporary password by email, and email sending is off on this server.");

        if (!TryTake(Proposals, adminId, _options.MaxProposalsPerMinute, TimeSpan.FromMinutes(1)))
            return Fail("You are giving commands too fast. Please wait a moment.");

        // ---- read the file (plain code, the model never sees it)
        var maxRows = kind == AiExcelTemplates.AccountsKind ? _options.MaxFileRows : _options.MaxRecipients;
        var read = AiExcelTemplates.Read(file, kind, maxRows);
        if (read.Error != null) return Fail(read.Error);

        var skipped = new List<(int Row, string Reason)>();
        var fingerprint = AiExcelTemplates.Fingerprint(file);
        PendingAiAction pending;
        string[] previewColumns;
        List<string[]> previewRows;
        string? note;

        if (kind == AiExcelTemplates.AccountsKind)
        {
            var valid = await ValidateAccountsAsync(read.Rows, skipped, ct);
            if (valid.Count == 0) return Fail(NothingValid(skipped));
            pending = new PendingAiAction
            {
                Id = Guid.NewGuid().ToString("N"), AdminId = adminId, AdminName = adminName, Type = AiActionType.CreateAccounts,
                AudienceLabel = "employee accounts from the Excel file", AccountRows = valid, SkippedRows = skipped.Count,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(_options.TtlMinutes),
            };
            previewColumns = ["Row", "Name", "Email", "Role", "Department"];
            previewRows = valid.Take(10).Select(r => new[] { r.ExcelRow.ToString(), r.FullName, r.Email, Roles.LabelFor(r.Role), r.DepartmentName }).ToList();
            note = _email.Mode != "smtp"
                ? "Email sending is not connected (SMTP is off). The welcome emails with the temporary passwords will only be saved as files on the server, so you must pass the passwords on yourself."
                : "Each person receives an email with a temporary password and must change it at the first sign-in.";
        }
        else
        {
            var valid = await ValidateMessagesAsync(read.Rows, notify, skipped, ct);
            if (valid.Count == 0) return Fail(NothingValid(skipped));

            // Rows without their own text use ONE common text. The model writes it from the Admin's note only (it never sees the rows).
            string commonSubject = "", commonBody = "";
            var needCommon = valid.Any(r => string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrWhiteSpace(r.Message));
            if (needCommon && string.IsNullOrEmpty(request))
            {
                foreach (var r in valid.Where(r => string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrWhiteSpace(r.Message)))
                    skipped.Add((r.ExcelRow, "no Subject / Message in the file and no common text typed in the command"));
                valid = valid.Where(r => !string.IsNullOrWhiteSpace(r.Subject) && !string.IsNullOrWhiteSpace(r.Message)).ToList();
                if (valid.Count == 0) return Fail(NothingValid(skipped) + " Type what the message should say in the command box, or fill Subject and Message in the file.");
            }
            else if (needCommon)
            {
                if (_chat.Provider is not ("ollama" or "openai")) return Fail("The AI assistant is turned off, so it cannot write the common text. Fill Subject and Message in the file instead.");
                var provider = _providers.First(p => (_chat.Provider == "ollama") == (p is OllamaChatProvider));
                (string Subject, string Body)? common;
                try { common = await AskCommonAsync(provider, [new("system", CommonTextPrompt), new("user", request!)], ct); }
                catch (AiUnavailableException ex) { return Fail(ex.Message); }
                if (common is null) return Fail("I could not write a message from that. Please say what the message should say, for example: tell them the system is down tomorrow.");
                commonSubject = OneLine(common.Value.Subject, 150);
                commonBody = BodyText(common.Value.Body, notify ? 500 : 2000);
                if (commonSubject.Length == 0 || commonBody.Length == 0) return Fail("The message needs a subject and some text. Please say what it should say.");
            }

            pending = new PendingAiAction
            {
                Id = Guid.NewGuid().ToString("N"), AdminId = adminId, AdminName = adminName, Type = AiActionType.BulkMessage, NotifyChannel = notify,
                AudienceLabel = "employees listed in the Excel file", Subject = commonSubject, Body = commonBody, MessageRows = valid, SkippedRows = skipped.Count,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(_options.TtlMinutes),
            };
            previewColumns = ["Row", "Name", "Email", "Subject"];
            previewRows = valid.Take(10).Select(r => new[] { r.ExcelRow.ToString(), r.Name, r.Email, string.IsNullOrWhiteSpace(r.Subject) ? commonSubject + " (common)" : r.Subject! }).ToList();
            note = !notify && _email.Mode != "smtp"
                ? "Email sending is not connected (SMTP is off). Approving will only save the messages as files on the server. Nothing will really be sent."
                : null;
        }

        _cache.Set(Key(pending.Id), pending, TimeSpan.FromMinutes(_options.TtlMinutes));
        var count = kind == AiExcelTemplates.AccountsKind ? pending.AccountRows.Count : pending.MessageRows.Count;
        await _audit.LogAsync("AiActionProposed", "AiAction", pending.Id,
            $"{pending.Type} from an Excel file ({fingerprint}): {count} valid row(s), {skipped.Count} skipped");   // never names, emails or text

        var heading = pending.Type == AiActionType.CreateAccounts
            ? $"Create {count} employee account{(count == 1 ? "" : "s")}"
            : $"Send {(notify ? "a notification" : "an email")} to {count} {(count == 1 ? "person" : "people")} (their own row)";
        var skippedLines = skipped.OrderBy(s => s.Row).Take(15).Select(s => $"Row {s.Row}: {s.Reason}").ToList();
        if (skipped.Count > 15) skippedLines.Add($"... and {skipped.Count - 15} more skipped rows");

        var card = new AiActionCard(pending.Id, pending.Type == AiActionType.CreateAccounts ? "accounts" : "messages", heading, pending.AudienceLabel, count,
            [], pending.Subject, pending.Body, pending.ExpiresUtc, canFingerprint, canFace, note, previewColumns, previewRows, skippedLines, skipped.Count);
        var message = skipped.Count == 0
            ? "The file is valid. Please check it and approve it with your fingerprint or face."
            : $"{count} row{(count == 1 ? " is" : "s are")} valid and {skipped.Count} will be skipped. Please check it and approve it with your fingerprint or face.";
        return new AiActionResult(true, message, card);
    }

    private static string NothingValid(List<(int Row, string Reason)> skipped) =>
        "No row of the file can be used. " + string.Join(" ", skipped.OrderBy(s => s.Row).Take(5).Select(s => $"Row {s.Row}: {s.Reason}.")) +
        (skipped.Count > 5 ? $" ...and {skipped.Count - 5} more." : "");

    // =================================================================== checking the rows (plain code)

    private async Task<List<AccountRow>> ValidateAccountsAsync(List<(int ExcelRow, string[] Cells)> rows, List<(int Row, string Reason)> skipped, CancellationToken ct)
    {
        var departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.Id, d.Name, d.Code }).ToListAsync(ct);
        var wanted = rows.Select(r => r.Cells[1].Trim().ToUpperInvariant()).Where(e => e.Length > 0).Distinct().ToList();
        var existing = (await _db.Users.AsNoTracking().Where(u => u.NormalizedEmail != null && wanted.Contains(u.NormalizedEmail))
            .Select(u => u.NormalizedEmail!).ToListAsync(ct)).ToHashSet();
        var seen = new HashSet<string>();
        var valid = new List<AccountRow>();

        foreach (var (excelRow, c) in rows)
        {
            string name = c[0].Trim(), email = c[1].Trim(), roleText = c[2].Trim(), departmentText = c[3].Trim(), title = c[4].Trim();
            var problems = new List<string>();

            if (name.Length < 2 || name.Length > 150 || !name.Any(char.IsLetter)) problems.Add("Full name must be 2 to 150 characters");
            if (email.Length == 0 || email.Length > 256 || !EmailPattern.IsMatch(email)) problems.Add("Email is not a valid address");
            else if (existing.Contains(email.ToUpperInvariant())) problems.Add("this email already has an account");
            else if (!seen.Add(email.ToUpperInvariant())) problems.Add("this email is repeated in the file");

            var role = AccountRoles.FirstOrDefault(r => SameName(r, roleText) || SameName(Roles.LabelFor(r), roleText));
            if (role is null) problems.Add(SameName(roleText, Roles.Admin) || SameName(roleText, Roles.Supplier)
                ? "this role cannot be created from a file"
                : "Role is not one of: " + string.Join(", ", AccountRoles.Select(Roles.LabelFor)));

            var matches = departments.Where(d => SameName(d.Name, departmentText) || SameName(d.Code, departmentText)).ToList();
            if (matches.Count != 1) problems.Add(matches.Count == 0 ? "Department was not found among the active departments" : "Department matches more than one");

            var phone = NormalizePhone(c[5]);
            if (!PhonePattern.IsMatch(phone)) problems.Add("Phone must be a Jordanian mobile number like 0791234567");
            if (title.Length > 100) problems.Add("Job title is longer than 100 characters");

            if (problems.Count > 0) { skipped.Add((excelRow, string.Join("; ", problems))); continue; }
            valid.Add(new AccountRow(excelRow, name, email, role!, matches[0].Id, matches[0].Name, title.Length == 0 ? null : title, phone));
        }
        return valid;
    }

    /// <summary>Spaces and dashes removed; +962 / 00962 / 962 become 0; a number typed without its leading 0 (Excel drops it) gets it back.</summary>
    private static string NormalizePhone(string? raw)
    {
        var p = Regex.Replace(raw ?? "", @"[\s\-().]", "");
        if (p.StartsWith("+962")) p = "0" + p[4..];
        else if (p.StartsWith("00962")) p = "0" + p[5..];
        else if (p.StartsWith("962") && p.Length == 12) p = "0" + p[3..];
        else if (Regex.IsMatch(p, @"^7[789]\d{7}$")) p = "0" + p;
        return p;
    }

    private async Task<List<MessageRow>> ValidateMessagesAsync(List<(int ExcelRow, string[] Cells)> rows, bool notify, List<(int Row, string Reason)> skipped, CancellationToken ct)
    {
        // Employees only: active, with an email, never a supplier (by user type OR by role), the same rule as the audience of a typed command.
        var supplierRoleId = await _db.Roles.Where(r => r.Name == Roles.Supplier).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var wanted = rows.Select(r => r.Cells[0].Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();
        var people = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.UserType != "Supplier" && !_db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == supplierRoleId)
                        && wanted.Contains(u.Email.ToLower()))
            .Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        var byEmail = people.ToDictionary(p => p.Email!.ToLowerInvariant());
        var seen = new HashSet<string>();
        var valid = new List<MessageRow>();
        var bodyMax = notify ? 500 : 2000;

        foreach (var (excelRow, c) in rows)
        {
            string email = c[0].Trim(), subject = c[1].Trim(), message = c[2].Trim();
            if (email.Length == 0 || !EmailPattern.IsMatch(email)) { skipped.Add((excelRow, "Email is not a valid address")); continue; }
            if (!byEmail.TryGetValue(email.ToLowerInvariant(), out var person)) { skipped.Add((excelRow, "no active employee has this email (suppliers and outsiders cannot be messaged)")); continue; }
            if (!seen.Add(email.ToLowerInvariant())) { skipped.Add((excelRow, "this email is repeated in the file")); continue; }
            if (Regex.IsMatch(subject, @"[\r\n]")) { skipped.Add((excelRow, "Subject must be one line")); continue; }
            if (subject.Length > 150) { skipped.Add((excelRow, "Subject is longer than 150 characters")); continue; }
            if (message.Length > bodyMax) { skipped.Add((excelRow, $"Message is longer than {bodyMax} characters")); continue; }
            valid.Add(new MessageRow(excelRow, person.Id, string.IsNullOrWhiteSpace(person.FullName) ? person.Email! : person.FullName, person.Email!,
                subject.Length == 0 ? null : subject, message.Length == 0 ? null : message));
        }
        return valid;
    }

    // =================================================================== the common text (the only thing the model writes)

    private const string CommonTextPrompt = """
        You write ONE short message that will be sent to a list of employees. You do not send anything and you never see the list: a person reviews your text and must approve it.
        Write "subject" (one short line) and "body" (plain text, polite and clear, at most 120 words, in the same language as the request).
        Use only what the Admin wrote. Never invent facts, dates, links or names, and do not use placeholders like [name].
        Reply with ONE JSON object and nothing else, exactly in this shape: {"subject": string, "body": string}
        """;

    private static async Task<(string Subject, string Body)?> AskCommonAsync(IAiChatProvider provider, List<AiChatMessage> messages, CancellationToken ct)
    {
        var raw = await provider.CompleteAsync(messages, ct, json: true);
        var parsed = ParseCommon(raw);
        if (parsed is not null) return parsed;
        var retry = new List<AiChatMessage>(messages) { new("assistant", raw), new("user", "That was not valid. Reply again with ONLY the JSON object in the exact shape given.") };
        return ParseCommon(await provider.CompleteAsync(retry, ct, json: true));
    }

    private static (string Subject, string Body)? ParseCommon(string raw)
    {
        var text = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.IgnoreCase).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var subject = Str(doc.RootElement, "subject");
            var body = Str(doc.RootElement, "body");
            return subject.Length == 0 || body.Length == 0 ? null : (subject, body);
        }
        catch (JsonException) { return null; }
    }

    // =================================================================== 4. EXECUTE a file action (once, from the stored rows)

    private async Task<AiActionResult> ExecuteFileAsync(PendingAiAction a, ApplicationUser admin)
    {
        var results = new List<string[]>();
        int ok = 0, failed = 0;

        if (a.Type == AiActionType.CreateAccounts)
        {
            foreach (var r in a.AccountRows)
            {
                try
                {
                    // creates the account with a temporary password, emails it, rolls back if the email cannot be sent, audits it
                    var (result, _) = await _passwords.CreateEmployeeAsync(r.FullName, r.Email, r.Role, r.DepartmentId, r.JobTitle, r.Phone);
                    if (result.Succeeded) { ok++; results.Add([r.Email, "Created"]); }
                    else { failed++; results.Add([r.Email, "Not created: " + (result.Errors.FirstOrDefault()?.Description ?? "unknown reason")]); }
                }
                catch (Exception ex)
                {
                    failed++;
                    results.Add([r.Email, "Not created: unexpected error"]);
                    _log.LogWarning(ex, "AI action {Id}: could not create one account", a.Id);
                }
            }
        }
        else
        {
            foreach (var r in a.MessageRows)
            {
                var subject = string.IsNullOrWhiteSpace(r.Subject) ? a.Subject : r.Subject!;
                var body = string.IsNullOrWhiteSpace(r.Message) ? a.Body : r.Message!;
                try
                {
                    if (a.NotifyChannel)
                    {
                        await _notify.NotifyAsync(r.UserId, "AdminAnnouncement", subject, body);
                        ok++; results.Add([r.Email, "Notification sent"]);
                    }
                    else if (await _email.SendAsync(r.Email, subject, EmailTemplates.Announcement(subject, body, a.AdminName), CancellationToken.None))
                    { ok++; results.Add([r.Email, "Email sent"]); }
                    else { failed++; results.Add([r.Email, "Could not be sent"]); }
                }
                catch (Exception ex)
                {
                    failed++;
                    results.Add([r.Email, "Could not be sent"]);
                    _log.LogWarning(ex, "AI action {Id}: could not reach one recipient", a.Id);
                }
            }
        }

        lock (a) { a.State = ok == 0 ? AiActionState.Failed : AiActionState.Done; }
        await _audit.LogAsync("AiActionExecuted", "AiAction", a.Id, $"{a.Type} from an Excel file: done {ok}, failed {failed}, skipped {a.SkippedRows}");

        var smtpOff = _email.Mode != "smtp";
        string message;
        if (a.Type == AiActionType.CreateAccounts)
            message = ok == 0 ? "No account could be created. See the list below."
                : failed == 0 ? $"Done. {ok} account{(ok == 1 ? " was" : "s were")} created." + (smtpOff ? " Email is not connected: the welcome emails with the temporary passwords were only saved as files on the server." : " Each person received an email with a temporary password.")
                : $"{ok} of {ok + failed} accounts were created. {failed} could not be created (see the list below).";
        else
            message = ok == 0 ? "No message could be sent. See the list below."
                : failed == 0 ? (a.NotifyChannel ? $"Done. {ok} notification{(ok == 1 ? " was" : "s were")} sent." : smtpOff ? $"Done, but email is not connected (SMTP is off): {ok} message{(ok == 1 ? "" : "s")} saved as files and NOT really sent." : $"Done. {ok} email{(ok == 1 ? " was" : "s were")} sent.")
                : $"{ok} of {ok + failed} messages were sent. {failed} could not be reached (see the list below).";
        return new AiActionResult(ok > 0, message, Sent: ok, Failed: failed, State: ok == 0 ? "Failed" : "Done", ResultRows: results);
    }

    /// <summary>Allows <paramref name="count"/> more takes inside the window, all or nothing.</summary>
    private static bool TryTakeMany(ConcurrentDictionary<string, Queue<DateTime>> store, string key, int count, int max, TimeSpan window)
    {
        var queue = store.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            var now = DateTime.UtcNow;
            while (queue.Count > 0 && now - queue.Peek() > window) queue.Dequeue();
            if (queue.Count + count > max) return false;
            for (var i = 0; i < count; i++) queue.Enqueue(now);
            return true;
        }
    }
}
