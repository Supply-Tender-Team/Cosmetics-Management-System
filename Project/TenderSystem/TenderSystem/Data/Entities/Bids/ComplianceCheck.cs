using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Bids;

/// <summary>Mandatory-requirement check result for a bid.</summary>
// OWNER: Dev 5  |  table: ComplianceChecks  |  design: docs-site (Schema Diagram)
public class ComplianceCheck
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public string CheckedByUserId { get; set; } = string.Empty;
    /// <summary>Allowed values: Compliant, Non-Compliant, Pending Clarification.</summary>
    [StringLength(30)]
    public string Result { get; set; } = string.Empty;
    [StringLength(1000)]
    public string? Reason { get; set; }
    public DateTime CheckedAt { get; set; }

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public ApplicationUser CheckedByUser { get; set; } = null!;
}
