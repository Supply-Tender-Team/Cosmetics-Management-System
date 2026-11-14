namespace TenderSystem.Services.Ai;

public enum AiActionType { Email, Notify }

/// <summary>
/// Proposed -> Approved -> Done | Failed. Or Cancelled / Expired. An action can only leave Proposed ONCE, so it cannot run twice.
/// </summary>
public enum AiActionState { Proposed, Approved, Done, Failed, Cancelled, Expired }

public sealed record AiActionRecipient(string UserId, string Name, string Email);

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

    // changed only inside lock (this)
    public AiActionState State { get; set; } = AiActionState.Proposed;
    public int IdentityFailures { get; set; }
    public int Tries { get; set; }
    public string? ApprovedWith { get; set; }
}

/// <summary>What the chat shows before anything is sent: exactly what will happen and to whom.</summary>
public sealed record AiActionCard(
    string ActionId, string Type, string Heading, string Audience, int RecipientCount, IReadOnlyList<string> SampleNames,
    string Subject, string Body, DateTime ExpiresUtc, bool CanFingerprint, bool CanFace);

public sealed record AiActionResult(bool Ok, string Message, AiActionCard? Card = null, int Sent = 0, int Failed = 0, string? State = null);
