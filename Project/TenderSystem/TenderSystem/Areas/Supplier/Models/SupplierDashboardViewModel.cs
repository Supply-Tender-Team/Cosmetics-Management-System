namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierDashboardViewModel
{
    public string SupplierName { get; init; } = "Supplier";
    public string VerificationStatus { get; init; } = "Pending";
    public int PendingInvitations { get; init; }
    public int SubmittedOffers { get; init; }
    public int OffersUnderReview { get; init; }
    public int ApprovedOffers { get; init; }
    public IReadOnlyList<SupplierInvitationRow> RecentInvitations { get; init; } = [];
    public IReadOnlyList<SupplierOfferRow> RecentOffers { get; init; } = [];
}

public sealed class SupplierOfferRow
{
    public int Id { get; init; }
    public string TenderNumber { get; init; } = "";
    public string TenderTitle { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal TotalPrice { get; init; }
    public string Currency { get; init; } = "";
    public DateTime? SubmittedAt { get; init; }
}

public sealed class SupplierInvitationRow
{
    public int Id { get; init; }
    public int TenderId { get; init; }
    public string TenderNumber { get; init; } = "";
    public string TenderTitle { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public bool CanRespond { get; init; }
}
