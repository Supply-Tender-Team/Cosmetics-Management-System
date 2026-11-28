using System.Data.Common;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Messaging;

namespace TenderSystem.Services.Email;

public static class EmailOutboxStatus
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

/// <summary>EML-01 settings (.env): EMAIL_OUTBOX_ENABLED, _INTERVAL_SECONDS, _MAX_ATTEMPTS, _BATCH, _LOCK_MINUTES.</summary>
public sealed class EmailOutboxOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 10;
    public int MaxAttempts { get; set; } = 5;
    public int Batch { get; set; } = 20;
    public int LockMinutes { get; set; } = 10;
}

/// <param name="DedupKey">Unique per email that must exist once, for example "award-winner:{tenderId}:{supplierProfileId}".</param>
/// <param name="Category">For reports, for example "AwardWinner".</param>
/// <param name="HtmlBody">The finished HTML (for example from EmailTemplates.Announcement).</param>
/// <param name="RelatedEntity">Optional: what it is about, for example "Tender".</param>
/// <param name="RelatedId">Optional: the id of that record, for example "42".</param>
public sealed record EmailOutboxRequest(string DedupKey, string Category, string ToEmail, string Subject, string HtmlBody, string? RelatedEntity = null, string? RelatedId = null);

/// <summary>Created = true: the email was queued now. Created = false: this DedupKey was already queued earlier (nothing new was added). Error is set when the request is invalid.</summary>
public sealed record EmailOutboxEnqueueResult(bool Created, int Id, string Status, string? Error);

public sealed record EmailOutboxInfo(int Id, string DedupKey, string Category, string ToEmail, string Status, int Attempts, DateTime CreatedAt, DateTime? SentAt, string? LastError);

/// <summary>
/// EML-01: put an email in the outbox instead of sending it inside your request.
///
///   var queued = await _outbox.EnqueueAsync(new EmailOutboxRequest($"award-winner:{tenderId}:{supplierId}", "AwardWinner", email, subject, html, "Tender", tenderId.ToString()), ct);
///   if (queued.Error is not null) { /* your request is wrong */ }   // queued.Created == false means "already queued, nothing to do"
///
/// A background worker (EmailOutboxWorker) sends it, retries failures with growing delays, and records the result.
/// Duplicate prevention is stored in the database (a unique DedupKey), so it survives restarts and parallel requests.
/// If you call it inside your own database transaction, the email is queued only when your transaction commits.
/// It sends nothing itself and it does not decide who may receive the email: that is the caller's job.
/// </summary>
public sealed class EmailOutboxService
{
    private readonly ApplicationDbContext _db;

    public EmailOutboxService(ApplicationDbContext db) => _db = db;

    public async Task<EmailOutboxEnqueueResult> EnqueueAsync(EmailOutboxRequest request, CancellationToken ct = default)
    {
        var subject = OneLine(request.Subject);
        var error = Check(request, subject);
        if (error is not null) return new EmailOutboxEnqueueResult(false, 0, "", error);

        var key = request.DedupKey.Trim();
        var now = DateTime.UtcNow;
        var related = request.RelatedEntity;
        var relatedId = request.RelatedId;
        int inserted;
        try
        {
            // One statement: insert only when the key does not exist. HOLDLOCK keeps two parallel requests from both inserting;
            // the unique index is the last guard.
            inserted = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO EmailOutbox (DedupKey, Category, ToEmail, Subject, HtmlBody, Status, Attempts, NextAttemptAt, RelatedEntity, RelatedId, CreatedAt)
                SELECT {key}, {request.Category.Trim()}, {request.ToEmail.Trim()}, {subject}, {request.HtmlBody}, {EmailOutboxStatus.Pending}, 0, {now}, {related}, {relatedId}, {now}
                WHERE NOT EXISTS (SELECT 1 FROM EmailOutbox WITH (UPDLOCK, HOLDLOCK) WHERE DedupKey = {key})", ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            inserted = 0;   // another request queued the same key at the same moment
        }

        var row = await _db.EmailOutbox.AsNoTracking().Where(x => x.DedupKey == key).Select(x => new { x.Id, x.Status }).FirstAsync(ct);
        return new EmailOutboxEnqueueResult(inserted == 1, row.Id, row.Status, null);
    }

    public Task<EmailOutboxInfo?> FindAsync(string dedupKey, CancellationToken ct = default) =>
        _db.EmailOutbox.AsNoTracking().Where(x => x.DedupKey == dedupKey)
            .Select(x => new EmailOutboxInfo(x.Id, x.DedupKey, x.Category, x.ToEmail, x.Status, x.Attempts, x.CreatedAt, x.SentAt, x.LastError))
            .FirstOrDefaultAsync(ct);

    /// <summary>The emails about one record, for example ("Tender", "42"), for reports and screens.</summary>
    public Task<List<EmailOutboxInfo>> ForRecordAsync(string relatedEntity, string relatedId, CancellationToken ct = default) =>
        _db.EmailOutbox.AsNoTracking().Where(x => x.RelatedEntity == relatedEntity && x.RelatedId == relatedId).OrderBy(x => x.Id)
            .Select(x => new EmailOutboxInfo(x.Id, x.DedupKey, x.Category, x.ToEmail, x.Status, x.Attempts, x.CreatedAt, x.SentAt, x.LastError))
            .ToListAsync(ct);

    /// <summary>The first problem with a request, or null when it is fine. (Public so the rules can be tested without a database.)</summary>
    public static string? Check(EmailOutboxRequest r, string subject)
    {
        if (string.IsNullOrWhiteSpace(r.DedupKey) || r.DedupKey.Trim().Length > 200) return "The dedup key is required (at most 200 characters).";
        if (string.IsNullOrWhiteSpace(r.Category) || r.Category.Trim().Length > 50) return "The category is required (at most 50 characters).";
        var to = r.ToEmail?.Trim() ?? "";
        if (to.Length is 0 or > 256 || !MailAddress.TryCreate(to, out var address) || address.Address != to) return "The recipient is not a valid email address.";
        if (subject.Length == 0) return "The subject is required.";
        if (string.IsNullOrWhiteSpace(r.HtmlBody) || r.HtmlBody.Length > 500_000) return "The body is required (at most 500,000 characters).";
        if ((r.RelatedEntity?.Length ?? 0) > 30 || (r.RelatedId?.Length ?? 0) > 50) return "RelatedEntity is at most 30 characters and RelatedId at most 50.";
        return null;
    }

    /// <summary>One line only (a line break in a subject could inject email headers), cut to 200 characters.</summary>
    public static string OneLine(string? text)
    {
        var line = string.Join(' ', (text ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length <= 200 ? line : line[..200].TrimEnd();
    }
}

/// <summary>
/// Sends the due emails, one pass at a time. Safe to run in several places at once: a row is claimed with one UPDATE (READPAST skips
/// rows that another worker holds), and the final result is written only if the row is still the worker's own.
/// "Exactly once" is not possible with a mail server: if the application stops between handing the email to the server and saving
/// "Sent", the email is sent again after the lock expires. The lock is long (EMAIL_OUTBOX_LOCK_MINUTES) to make that very rare.
/// </summary>
public sealed class EmailOutboxProcessor
{
    // Minutes to wait after the 1st, 2nd, 3rd... failed attempt.
    private static readonly int[] BackoffMinutes = [1, 5, 15, 60, 240];

    /// <summary>How long to wait before the next try, after this many attempts have failed: 1, 5, 15, 60, then 240 minutes every time.</summary>
    public static TimeSpan RetryDelay(int failedAttempts) =>
        TimeSpan.FromMinutes(BackoffMinutes[Math.Clamp(failedAttempts - 1, 0, BackoffMinutes.Length - 1)]);

    private readonly ApplicationDbContext _db;
    private readonly EmailOutboxOptions _options;
    private readonly ILogger<EmailOutboxProcessor> _log;

    public EmailOutboxProcessor(ApplicationDbContext db, IOptions<EmailOutboxOptions> options, ILogger<EmailOutboxProcessor> log)
    {
        _db = db;
        _options = options.Value;
        _log = log;
    }

    /// <returns>How many emails this pass took.</returns>
    /// <param name="deliveryMode">How the send function delivers ("smtp", "preview" or "off", from EmailService.DeliveryMode). It is saved on every row that gets Sent.</param>
    public async Task<int> ProcessOnceAsync(Func<string, string, string, CancellationToken, Task<bool>> send, string? deliveryMode = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.AddMinutes(Math.Max(1, _options.LockMinutes));
        var ids = await ClaimAsync(now, lockUntil, Math.Clamp(_options.Batch, 1, 500), ct);

        foreach (var id in ids)
        {
            var mail = await _db.EmailOutbox.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.ToEmail, x.Subject, x.HtmlBody, x.Attempts }).FirstOrDefaultAsync(ct);
            if (mail is null) continue;

            bool ok;
            string? problem = null;
            try
            {
                ok = await send(mail.ToEmail, mail.Subject, mail.HtmlBody, ct);
                if (!ok) problem = "The mail server did not accept the email.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The application is stopping: give the row back without counting this attempt.
                await _db.EmailOutbox.Where(x => x.Id == id && x.Status == EmailOutboxStatus.Sending && x.LockedUntil == lockUntil)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, EmailOutboxStatus.Pending).SetProperty(x => x.LockedUntil, (DateTime?)null)
                        .SetProperty(x => x.Attempts, x => x.Attempts - 1).SetProperty(x => x.NextAttemptAt, DateTime.UtcNow), CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                ok = false;
                problem = Cut(ex.GetType().Name + ": " + ex.Message, 500);   // never the email text
                _log.LogWarning(ex, "Outbox email {Id} could not be sent", id);
            }

            await FinishAsync(id, lockUntil, mail.Attempts, ok, problem, deliveryMode, ct);
        }
        return ids.Count;
    }

    private async Task<List<int>> ClaimAsync(DateTime now, DateTime lockUntil, int batch, CancellationToken ct)
    {
        var ids = new List<int>();
        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = _db.Database.GetDbConnection().CreateCommand();
            cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = @"
                WITH due AS (
                    SELECT TOP (@batch) *
                    FROM EmailOutbox WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE (Status = @pending AND NextAttemptAt <= @now) OR (Status = @sending AND LockedUntil < @now)
                    ORDER BY NextAttemptAt, Id)
                UPDATE due SET Status = @sending, LockedUntil = @lock, Attempts = Attempts + 1
                OUTPUT inserted.Id";
            Add(cmd, "@batch", batch);
            Add(cmd, "@pending", EmailOutboxStatus.Pending);
            Add(cmd, "@sending", EmailOutboxStatus.Sending);
            Add(cmd, "@now", now);
            Add(cmd, "@lock", lockUntil);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt32(0));
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
        return ids;
    }

    private async Task FinishAsync(int id, DateTime lockUntil, int attempts, bool ok, string? problem, string? deliveryMode, CancellationToken ct)
    {
        var mine = _db.EmailOutbox.Where(x => x.Id == id && x.Status == EmailOutboxStatus.Sending && x.LockedUntil == lockUntil);
        if (ok)
        {
            await mine.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, EmailOutboxStatus.Sent).SetProperty(x => x.SentAt, DateTime.UtcNow)
                .SetProperty(x => x.DeliveryMode, deliveryMode).SetProperty(x => x.HtmlBody, "").SetProperty(x => x.LockedUntil, (DateTime?)null).SetProperty(x => x.LastError, (string?)null), ct);
        }
        else if (attempts >= Math.Max(1, _options.MaxAttempts))
        {
            await mine.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, EmailOutboxStatus.Failed)
                .SetProperty(x => x.LockedUntil, (DateTime?)null).SetProperty(x => x.LastError, problem), ct);
        }
        else
        {
            var next = DateTime.UtcNow + RetryDelay(attempts);
            await mine.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, EmailOutboxStatus.Pending).SetProperty(x => x.NextAttemptAt, next)
                .SetProperty(x => x.LockedUntil, (DateTime?)null).SetProperty(x => x.LastError, problem), ct);
        }
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        if (value is DateTime) p.DbType = System.Data.DbType.DateTime2;   // the column is datetime2: a plain DateTime parameter would be rounded and no longer match
        cmd.Parameters.Add(p);
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>Runs the processor every few seconds. It never stops because of an error, and it does nothing when EMAIL_OUTBOX_ENABLED=false.</summary>
public sealed class EmailOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOutboxOptions _options;
    private readonly ILogger<EmailOutboxWorker> _log;

    public EmailOutboxWorker(IServiceScopeFactory scopes, IOptions<EmailOutboxOptions> options, ILogger<EmailOutboxWorker> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!_options.Enabled)
        {
            _log.LogInformation("Email outbox worker is off (EMAIL_OUTBOX_ENABLED=false).");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { return; }   // let the database setup finish first
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.IntervalSeconds, 2, 3600));

        while (!stop.IsCancellationRequested)
        {
            var wait = interval;
            try
            {
                int handled, rounds = 0;
                do
                {
                    using var scope = _scopes.CreateScope();
                    var email = scope.ServiceProvider.GetRequiredService<EmailService>();
                    handled = await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>()
                        .ProcessOnceAsync(email.SendAsync, email.DeliveryMode, stop);
                } while (handled >= Math.Clamp(_options.Batch, 1, 500) && ++rounds < 20 && !stop.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Email outbox pass failed. Is the EmailOutbox migration applied? Trying again later.");
                wait = TimeSpan.FromSeconds(Math.Max(60, interval.TotalSeconds));
            }

            try { await Task.Delay(wait, stop); } catch (OperationCanceledException) { break; }
        }
    }
}
