namespace TenderSystem.Services.Ai;

/// <summary>Email / Notify: one text to a group of employees (AI-04). CreateAccounts / BulkMessage: an Excel file made from a server template (AI-09).</summary>
public enum AiActionType { Email, Notify, CreateAccounts, BulkMessage }

/// <summary>
/// Proposed -> Approved -> Done | Failed. Or Cancelled / Expired. An action can only leave Proposed ONCE, so it cannot run twice.
/// </summary>
public enum AiActionState { Proposed, Approved, Done, Failed, Cancelled, Expired }

public sealed record AiActionRecipient(string UserId, string Name, string Email);

/// <summary>One valid row of the "create accounts" template, checked by the server. Only these rows are ever executed.</summary>
public sealed record AccountRow(int ExcelRow, string FullName, string Email, string Role, int DepartmentId, string DepartmentName, string? JobTitle, string Phone);

/// <summary>One valid row of the "messages" template. Subject / Message are this row's own text, or empty to use the common text.</summary>
public sealed record MessageRow(int ExcelRow, string UserId, string Name, string Email, string? Subject, string? Message);

/// <summary>
/// An action waiting for the Admin's fingerprint or face. It lives in memory for a few minutes. Subject, body and recipients are
/// fixed here: approval and execution use ONLY this object, never anything the browser sends afterwards.
/// </summary>
public sealed class PendingAiAction
{
    public string Id { get; init; } = "";
    public string AdminId { get; init; } = "";
    public string AdminName { get; init; } = "";
    public AiActionType Type { get; init; }
    public string AudienceLabel { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Body { get; init; } = "";
    public IReadOnlyList<AiActionRecipient> Recipients { get; init; } = [];
    public DateTime ExpiresUtc { get; init; }

    // AI-09 (Excel file actions): the valid rows, fixed on the server. The browser can never change them.
    public IReadOnlyList<AccountRow> AccountRows { get; init; } = [];
    public IReadOnlyList<MessageRow> MessageRows { get; init; } = [];
    public bool NotifyChannel { get; init; }
    public int SkippedRows { get; init; }

    // changed only inside lock (this)
    public AiActionState State { get; set; } = AiActionState.Proposed;
    public int IdentityFailures { get; set; }
    public int Tries { get; set; }
    public string? ApprovedWith { get; set; }
}

/// <summary>What the chat shows before anything is sent: exactly what will happen and to whom.</summary>
public sealed record AiActionCard(
    string ActionId, string Type, string Heading, string Audience, int RecipientCount, IReadOnlyList<string> SampleNames,
    string Subject, string Body, DateTime ExpiresUtc, bool CanFingerprint, bool CanFace, string? Note = null,
    IReadOnlyList<string>? PreviewColumns = null, IReadOnlyList<string[]>? PreviewRows = null, IReadOnlyList<string>? Skipped = null, int SkippedCount = 0);

public sealed record AiActionResult(bool Ok, string Message, AiActionCard? Card = null, int Sent = 0, int Failed = 0, string? State = null, IReadOnlyList<string[]>? ResultRows = null);
