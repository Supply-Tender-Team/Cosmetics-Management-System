using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Tenders;

/// <summary>The invitation sent to a supplier and the supplier's answer.</summary>
// OWNER: Nada  |  table: Invitations  |  design: docs-site (Schema Diagram)
public class Invitation
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int SupplierProfileId { get; set; }
    /// <summary>Allowed values: Pending, Accepted, Declined, Expired.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Pending";
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    [StringLength(500)]
    public string? DeclineReason { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public SupplierProfile SupplierProfile { get; set; } = null!;
}
