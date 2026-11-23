namespace TenderSystem.Areas.Supplier.Models;

public sealed class DemoBankVerificationViewModel
{
    public string ScannedPayload { get; init; } = string.Empty;
    public bool IsKnownTestGuarantee { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Beneficiary { get; init; } = string.Empty;
    public string IssuingBank { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string ExpiresAt { get; init; } = string.Empty;
}
