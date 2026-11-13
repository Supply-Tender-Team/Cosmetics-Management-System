using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Biometrics;

/// <summary>FINGERPRINT (and device unlock). One row per registered WebAuthn passkey. Only the PUBLIC key is stored.</summary>
// OWNER: Wadea  |  table: BiometricCredentials  |  design: docs-site (Schema Diagram)
public class BiometricCredential
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public byte[] CredentialId { get; set; } = [];
    public byte[] PublicKey { get; set; } = [];
    public long SignCount { get; set; }
    [StringLength(100)]
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Allowed values: platform, cross-platform.</summary>
    [StringLength(30)]
    public string AuthenticatorType { get; set; } = string.Empty;
    [StringLength(100)]
    public string? Transports { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
