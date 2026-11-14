using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Bids;

/// <summary>
/// The Sourcing Officer's review of ONE requirement for ONE offer ("Tax certificate: NotMet, nothing uploaded").
/// The overall decision about the offer stays in ComplianceChecks. It is the Officer's decision, not a count of Met results.
/// A review is updated in place when the Officer reviews again; write an audit entry for each change.
/// </summary>
// OWNER: Deyaa (the Compliance screens)  |  table: RequirementReviews  |  added by Wadea (DB-04)  |  design: docs-site (Schema Diagram)
public class RequirementReview
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public int RequirementId { get; set; }
    public string ReviewedByUserId { get; set; } = string.Empty;
    /// <summary>Allowed values: Met, NotMet, NeedsReview (see Statuses.RequirementResult).</summary>
    [StringLength(20)]
    public string Result { get; set; } = "NeedsReview";
    [StringLength(1000)]
    public string? Notes { get; set; }
    public DateTime ReviewedAt { get; set; }

    // Navigation properties. One review per offer and requirement (unique index BidId + RequirementId).
    public Bid Bid { get; set; } = null!;
    public TenderRequirement Requirement { get; set; } = null!;
    public ApplicationUser ReviewedByUser { get; set; } = null!;
}
