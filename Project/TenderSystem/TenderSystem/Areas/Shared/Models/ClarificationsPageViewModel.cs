namespace TenderSystem.Areas.Shared.Models;

public sealed class ClarificationsPageViewModel
{
    public string Filter { get; init; } = "all";
    public int TotalCount { get; init; }
    public int WaitingForMeCount { get; init; }
    public int WaitingForReplyCount { get; init; }
    public int ResolvedCount { get; init; }
    public List<ClarificationThreadRow> Threads { get; init; } = [];

    public sealed record ClarificationThreadRow(
        int Id,
        string Subject,
        string Status,
        string Context,
        string OtherParty,
        string Preview,
        DateTime LastActivityAt,
        bool IsIncoming);
}

public sealed class ClarificationDetailsViewModel
{
    public int Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public string OtherParty { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool CanReply { get; init; }
    public bool CanResolve { get; init; }
    public bool ReplyAccessUnavailable { get; init; }
    public string ReplyDraft { get; init; } = string.Empty;
    public string? ReplyError { get; init; }
    public List<ClarificationMessageItem> Messages { get; init; } = [];

    public sealed record ClarificationMessageItem(
        string SenderName,
        string Body,
        DateTime SentAt,
        bool IsCurrentUser);
}
