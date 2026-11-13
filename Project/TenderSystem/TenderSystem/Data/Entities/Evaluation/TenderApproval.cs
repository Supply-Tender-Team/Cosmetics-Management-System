using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>Manager and Top Committee decisions with comments.</summary>
// OWNER: Dev 6  |  table: TenderApprovals  |  design: docs-site (Schema Diagram)
public class TenderApproval
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int ReportId { get; set; }
    public string DecidedByUserId { get; set; } = string.Empty;
    /// <summary>Allowed values: Manager, TopCommittee.</summary>
    [StringLength(20)]
    public string Level { get; set; } = string.Empty;
    /// <summary>Allowed values: Approved, Rejected, Returned.</summary>
    [StringLength(20)]
    public string Decision { get; set; } = string.Empty;
    public int? SelectedBidId { get; set; }
    [StringLength(1000)]
    public string Comment { get; set; } = string.Empty;
    [StringLength(200)]
    public string? SignatureHash { get; set; }
    public DateTime DecidedAt { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public EvaluationReport Report { get; set; } = null!;
    public ApplicationUser DecidedByUser { get; set; } = null!;
    public Bid? SelectedBid { get; set; }
}
