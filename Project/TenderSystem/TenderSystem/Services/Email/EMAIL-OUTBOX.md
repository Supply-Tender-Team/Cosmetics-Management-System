# Email outbox (EML-01)

Owner: Wadea (`Services/Email`). First user: the award emails (Abdallah's Top Committee flow). Anyone can use it.

## What it solves
Sending an email inside a request has three problems: the mail server can be slow or down, the request may fail after the email left, and a retry may send it twice. The outbox puts the email in a table first. A background worker sends it, retries failures, and records the result. The same email can never be queued twice.

## How to use it (inject `EmailOutboxService`)
```csharp
var queued = await _outbox.EnqueueAsync(new EmailOutboxRequest(
    DedupKey: $"award-winner:{tenderId}:{supplierProfileId}",   // one value per email that must exist once
    Category: "AwardWinner",                                     // for reports
    ToEmail: email, Subject: subject, HtmlBody: html,            // html from EmailTemplates (for example Announcement)
    RelatedEntity: "Tender", RelatedId: tenderId.ToString()), ct);

if (queued.Error is not null) { /* the request is wrong: fix the caller */ }
// queued.Created == false means: already queued earlier. Nothing to do, nothing was added.
```
- It does **not** send, and it does **not** check who may receive the email. The caller decides the recipients.
- Call it **after** your business action is committed. If you call it inside your own transaction, the email is queued only when that transaction commits.
- Running the same code again is safe (the key prevents duplicates). That is the "catch-up" if a trigger crashed after the commit.

## Dedup key convention
`<kind>:<record id>:<recipient id>`, for example `award-winner:42:7`, `award-unsuccessful:42:9`. Never include text typed by a user. At most 200 characters.

## What the worker does
- Every `EMAIL_OUTBOX_INTERVAL_SECONDS` (default 10) it takes the due rows (`Pending` and due, or `Sending` with an expired lock) in batches, marks them `Sending` with a lock, and calls `EmailService.SendAsync`.
- Sent: status `Sent`, time saved, **the body is emptied** (it can hold prices).
- Refused or error: back to `Pending` with the problem (never the email text) and a delay of 1, 5, 15, 60, then 240 minutes. After `EMAIL_OUTBOX_MAX_ATTEMPTS` (default 5) the status is `Failed` and it stays `Failed` (no automatic retry; look at `LastError`).
- Several workers (or several servers) at once are safe: a row is claimed with one statement, and a worker never overwrites a row that another worker took over.
- If the application stops while sending, the row goes back to `Pending` without using an attempt.
- Limit: "exactly once" does not exist with a mail server. If the application dies after the server accepted the email but before `Sent` was saved, the email is sent again after the lock expires (`EMAIL_OUTBOX_LOCK_MINUTES`, default 10). That is rare.

## Reading the result (reports, screens)
`EmailOutboxService.FindAsync(dedupKey)` and `ForRecordAsync("Tender", "42")` return status, attempts, sent time and the last error. The table is `EmailOutbox`.

## Settings (.env)
`EMAIL_OUTBOX_ENABLED` (default true), `EMAIL_OUTBOX_INTERVAL_SECONDS` (10), `EMAIL_OUTBOX_MAX_ATTEMPTS` (5), `EMAIL_OUTBOX_BATCH` (20), `EMAIL_OUTBOX_LOCK_MINUTES` (10).
Real sending still depends on `SMTP_ENABLED` and the `SMTP_*` keys. With `SMTP_ENABLED=false` in Development, "sent" means the email was saved as a file in `App_Data/emails`; outside Development it counts as a failed attempt.

## How it was delivered (`DeliveryMode`, EML-02)

When a row becomes `Sent` the worker also saves `DeliveryMode`: `smtp` (handed to the real mail server), `preview` (Development only: the email was just saved as a file in `App_Data/emails`, nobody received it) or `off`. It is filled automatically, nobody sets it by hand. Rows sent before this column existed stay empty, which means unknown. Reports must count a real delivery only when `Status = Sent` and `DeliveryMode = smtp`.
