using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Biometrics;

/// <summary>FACE. Stores an encrypted face template (numeric vector), never the photo.</summary>
// OWNER: Dev 2  |  table: FaceEnrollments  |  design: docs-site (Schema Diagram)
public class FaceEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public byte[] FaceTemplate { get; set; } = [];
    [StringLength(50)]
    public string ModelVersion { get; set; } = string.Empty;
    [Precision(5, 4)]
    public decimal LivenessScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
