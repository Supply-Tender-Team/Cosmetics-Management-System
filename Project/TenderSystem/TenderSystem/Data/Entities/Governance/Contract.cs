using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Governance;

/// <summary>
/// The final contract of an awarded tender (RPT-01). The final approval may create it as a Draft, but it never becomes Signed by itself:
/// a person must upload the document and finalize it. The agreed amount and currency are copied from the winning offer when the draft is created
/// and then belong to the contract: a later change of the offer never changes them.
/// </summary>
// OWNER: Wadea  |  table: Contracts
public class Contract
{
    public int Id { get; set; }

    /// <summary>Unique, for example CTR-TND-2026-0007 (CTR- plus the tender number, and -2, -3 if an earlier contract was cancelled).</summary>
    [StringLength(60)]
    public string ContractNumber { get; set; } = string.Empty;

    public int TenderId { get; set; }

    /// <summary>The winning offer (Bid) the contract is based on.</summary>
    public int AwardedBidId { get; set; }

    /// <summary>Allowed values: Draft, Signed, Cancelled. At most one contract per tender is not Cancelled.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Draft";

    [Precision(18, 2)]
    public decimal Amount { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>The date the contract was signed, UTC. Filled only when the status becomes Signed.</summary>
    public DateTime? SignedAt { get; set; }

    /// <summary>The stored contract file (path inside the uploads folder, like the other documents). Required before Signed.</summary>
    [StringLength(300)]
    public string? DocumentPath { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The person who finalized (Signed) the contract.</summary>
    public string? FinalizedByUserId { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public Bid AwardedBid { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ApplicationUser? FinalizedByUser { get; set; }
}
