namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierTenderDetailsViewModel
{
    public int Id { get; init; }
    public string TenderNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Terms { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime SubmissionDeadline { get; init; }
    public string InvitationStatus { get; init; } = string.Empty;
    public DateTime? InvitationExpiresAt { get; init; }
    public bool CanSubmitOffer { get; init; }
    public IReadOnlyList<SupplierTenderItemViewModel> Items { get; init; } = [];
}
