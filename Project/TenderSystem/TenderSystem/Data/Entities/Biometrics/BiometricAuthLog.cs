using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Biometrics;

/// <summary>Every login attempt (face, fingerprint, password) with result, for security and audit.</summary>
// OWNER: Dev 2  |  table: BiometricAuthLogs  |  design: docs-site (Schema Diagram)
public class BiometricAuthLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    /// <summary>Allowed values: Face, Fingerprint, Password.</summary>
    [StringLength(20)]
    public string Method { get; set; } = string.Empty;
    public bool Success { get; set; }
    [StringLength(200)]
    public string? FailureReason { get; set; }
    [StringLength(45)]
    public string IpAddress { get; set; } = string.Empty;
    [StringLength(300)]
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser? User { get; set; }
}
