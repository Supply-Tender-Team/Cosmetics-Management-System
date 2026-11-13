using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Identity;

/// <summary>Supplier company details and Admin verification status.</summary>
// OWNER: Abdallah  |  table: SupplierProfiles  |  design: docs-site (Schema Diagram)
public class SupplierProfile
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    [StringLength(200)]
    public string CompanyName { get; set; } = string.Empty;
    [StringLength(50)]
    public string TaxNumber { get; set; } = string.Empty;
    [StringLength(50)]
    public string CommercialRegNumber { get; set; } = string.Empty;
    [StringLength(80)]
    public string Country { get; set; } = string.Empty;
    [StringLength(80)]
    public string City { get; set; } = string.Empty;
    [StringLength(300)]
    public string Address { get; set; } = string.Empty;
    [StringLength(150)]
    public string ContactPerson { get; set; } = string.Empty;
    [StringLength(30)]
    public string ContactPhone { get; set; } = string.Empty;
    [StringLength(200)]
    public string? Website { get; set; }
    [StringLength(100)]
    public string Category { get; set; } = string.Empty;
    /// <summary>Allowed values: Pending, Approved, Rejected, Suspended.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Pending";
    [StringLength(500)]
    public string? StatusReason { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
    public ICollection<SupplierDocument> SupplierDocuments { get; set; } = new List<SupplierDocument>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    public ICollection<TenderSupplier> TenderSuppliers { get; set; } = new List<TenderSupplier>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}
