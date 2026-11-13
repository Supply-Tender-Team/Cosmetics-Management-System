using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Tenders;

/// <summary>Which suppliers were selected for a tender (many-to-many).</summary>
// OWNER: Dev 4  |  table: TenderSuppliers  |  design: docs-site (Schema Diagram)
public class TenderSupplier
{
    public int TenderId { get; set; }
    public int SupplierProfileId { get; set; }

    public DateTime AddedAt { get; set; }
    /// <summary>Allowed values: Selected, Participating, Withdrawn.</summary>
    [StringLength(20)]
    public string ParticipationStatus { get; set; } = "Selected";

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public SupplierProfile SupplierProfile { get; set; } = null!;
}
