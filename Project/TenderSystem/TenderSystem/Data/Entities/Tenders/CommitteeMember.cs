using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Tenders;

/// <summary>The 4 evaluation committee members of a tender.</summary>
// OWNER: Nada  |  table: CommitteeMembers  |  design: docs-site (Schema Diagram)
public class CommitteeMember
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public string UserId { get; set; } = string.Empty;
    /// <summary>Allowed values: SourcingManager, Department, Financial, Technical.</summary>
    [StringLength(30)]
    public string MemberRole { get; set; } = string.Empty;

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
