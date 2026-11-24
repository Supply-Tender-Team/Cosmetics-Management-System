using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Governance;

/// <summary>
/// One verification decision about ONE supplier legal document OR ONE bid guarantee (RPT-01). Exactly one of SupplierDocumentId / BidGuaranteeId is filled.
/// The table only grows: a changed decision is a NEW row, the old rows stay as history. The current decision is the row with the highest Id of that
/// document or guarantee. No row at all means "no verification recorded", never "Verified". Supplier approval does not write rows here.
/// Who writes: the Admin for supplier documents, Sourcing for guarantees (the controller checks the role). OCR / QR may support the review but never write a row alone.
/// </summary>
// OWNER: Wadea  |  table: DocumentVerifications
public class DocumentVerification
{
    public int Id { get; set; }

    public int? SupplierDocumentId { get; set; }
    public int? BidGuaranteeId { get; set; }

    /// <summary>Allowed values: Pending, Verified, Rejected.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>Allowed values: Manual, OcrAssisted, QrAssisted.</summary>
    [StringLength(20)]
    public string Method { get; set; } = "Manual";

    /// <summary>Why the decision was made. Required when the status is Rejected.</summary>
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>The real person who made the decision.</summary>
    public string VerifiedByUserId { get; set; } = string.Empty;

    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public SupplierDocument? SupplierDocument { get; set; }
    public BidGuarantee? BidGuarantee { get; set; }
    public ApplicationUser VerifiedByUser { get; set; } = null!;
}
