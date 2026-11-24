namespace TenderSystem.Areas.Sourcing.Models;

public sealed class SourcingDashboardViewModel
{
    public required List<(string Label, int Count, string Style)> RequestStatuses { get; init; }
    public int SubmittedRequestTotal => RequestStatuses.Sum(status => status.Count);
    public int ApprovedRequestTotal => RequestStatuses.Where(status => status.Style is "approved" or "converted").Sum(status => status.Count);
    public int ApprovalPercent => SubmittedRequestTotal == 0 ? 0 : (int)Math.Round(ApprovedRequestTotal * 100.0 / SubmittedRequestTotal);
    public int PendingInvitationCount { get; init; }
    public int ApprovedRequestCount { get; init; }
    public int ActiveTenderCount { get; init; }
    public int IncomingOfferCount { get; init; }
    public required List<SourcingDashboardRow> RecentRequests { get; init; }
    public required IReadOnlyList<SourcingClosingDay> ClosingDays { get; init; }
}

public sealed record SourcingDashboardRow(int Id, string Reference, string Title, DateTime Date, string Priority, string Status);

public sealed record SourcingClosingDay(DateTime Date, IReadOnlyList<SourcingTenderClosing> Tenders);

public sealed record SourcingTenderClosing(int Id, string Title, DateTime Deadline, TimeSpan TimeLeft, int OfferCount);
