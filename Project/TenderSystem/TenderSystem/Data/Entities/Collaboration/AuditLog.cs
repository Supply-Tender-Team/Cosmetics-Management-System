using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Collaboration;

/// <summary>Who did what and when (every offer, decision, approval).</summary>
// OWNER: Marwa  |  table: AuditLogs  |  design: docs-site (Schema Diagram)
public class AuditLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    [StringLength(100)]
    public string Action { get; set; } = string.Empty;
    [StringLength(100)]
    public string EntityName { get; set; } = string.Empty;
    [StringLength(50)]
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    [StringLength(45)]
    public string IpAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser? User { get; set; }
}
