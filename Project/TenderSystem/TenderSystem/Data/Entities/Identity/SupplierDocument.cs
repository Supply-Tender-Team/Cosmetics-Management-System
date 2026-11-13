using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TenderSystem.Data.Entities.Identity;

/// <summary>Legal files uploaded by a supplier (license, tax certificate, commercial register).</summary>
// OWNER: Dev 3  |  table: SupplierDocuments  |  design: docs-site (Schema Diagram)
public class SupplierDocument
{
    public int Id { get; set; }
    public int SupplierProfileId { get; set; }
    [StringLength(50)]
    public string DocType { get; set; } = string.Empty;
    [StringLength(300)]
    public string FilePath { get; set; } = string.Empty;
    [Column(TypeName = "date")]
    public DateTime? ExpiryDate { get; set; }
    public DateTime UploadedAt { get; set; }

    // Navigation properties
    public SupplierProfile SupplierProfile { get; set; } = null!;
}
