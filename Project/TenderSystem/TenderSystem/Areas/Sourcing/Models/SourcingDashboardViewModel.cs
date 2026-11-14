namespace TenderSystem.Areas.Sourcing.Models;

public sealed class SourcingDashboardViewModel
{
    public int PendingInvitationCount { get; init; }
    public int ApprovedRequestCount { get; init; }
    public int ActiveTenderCount { get; init; }
    public int IncomingOfferCount { get; init; }
    public required List<SourcingDashboardRow> ApprovedRequests { get; init; }
    public required List<SourcingDashboardRow> ActiveTenders { get; init; }
    public required List<SourcingDashboardRow> IncomingOffers { get; init; }
}

public sealed record SourcingDashboardRow(string Reference, string Title, string Detail, DateTime Date, string Status);
