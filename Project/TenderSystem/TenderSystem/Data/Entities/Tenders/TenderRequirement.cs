using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Bids;

namespace TenderSystem.Data.Entities.Tenders;

/// <summary>
/// One requirement of a tender, stored on its own (instead of one text block in Tenders.Terms).
/// A requirement is a PASS or FAIL rule ("valid tax certificate", "delivery within 14 days"). Do not confuse it with
/// EvaluationCriteria, which are weighted SCORES used to compare offers.
/// </summary>
// OWNER: Nada (the tender screens create and edit it)  |  table: TenderRequirements  |  added by Wadea (DB-04)  |  design: docs-site (Schema Diagram)
public class TenderRequirement
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;
    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;
    /// <summary>Allowed values: Document, Delivery, Validity, Technical, Other (see Statuses.RequirementType).</summary>
    [StringLength(20)]
    public string Type { get; set; } = "Other";
    /// <summary>
    /// Document requirements only: the BidDocuments.DocType that satisfies it (for example "Tax certificate"), so the
    /// system can tell at once whether the supplier uploaded it. Empty for the other types.
    /// </summary>
    [StringLength(100)]
    public string? DocumentType { get; set; }
    /// <summary>False = nice to have. A mandatory requirement that is not met normally makes the offer non-compliant.</summary>
    public bool IsMandatory { get; set; } = true;
    /// <summary>Order on the screens.</summary>
    public int SortOrder { get; set; }
    /// <summary>
    /// Set when the tender is published. A locked requirement must not be edited or deleted, because offers are reviewed
    /// against it (the database also refuses to delete a requirement that already has reviews).
    /// </summary>
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties. (Tender has no collection on purpose, so Nada's entity file stays untouched:
    // use _db.TenderRequirements.Where(r => r.TenderId == id).)
    public Tender Tender { get; set; } = null!;
    public ICollection<RequirementReview> Reviews { get; set; } = new List<RequirementReview>();
}
