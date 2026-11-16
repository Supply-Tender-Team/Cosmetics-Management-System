using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Data.Entities.Identity;

/// <summary>
/// One of the three fixed evaluation committee positions (every position except the Department Manager).
/// The Admin puts one Evaluation Committee account in each position; new tenders take these people when they are saved or published.
/// </summary>
// OWNER: Abdallah  |  table: StandingCommitteeMembers
public class StandingCommitteeMember
{
    public int Id { get; set; }

    /// <summary>Allowed values: SourcingManager (Department Financial Officer), Technical (Sourcing Department Representative), Financial (Financial Manager). See CommitteePositions.</summary>
    [StringLength(30)]
    public string PositionCode { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The Admin who made the last change.</summary>
    public string? UpdatedByUserId { get; set; }

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
