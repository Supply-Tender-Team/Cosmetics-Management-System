using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>Each Top Committee member's vote (group decision record).</summary>
// OWNER: Dana  |  table: TopCommitteeVotes  |  design: docs-site (Schema Diagram)
public class TopCommitteeVote
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public int? SelectedBidId { get; set; }
    /// <summary>Allowed values: Approve, Reject, NeedMoreInfo.</summary>
    [StringLength(20)]
    public string Vote { get; set; } = string.Empty;
    [StringLength(1000)]
    public string? Comment { get; set; }
    public DateTime VotedAt { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public ApplicationUser MemberUser { get; set; } = null!;
    public Bid? SelectedBid { get; set; }
}
