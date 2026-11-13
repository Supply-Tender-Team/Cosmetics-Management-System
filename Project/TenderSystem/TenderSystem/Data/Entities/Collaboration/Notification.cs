using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Collaboration;

/// <summary>In-app alerts for users.</summary>
// OWNER: Deyaa  |  table: Notifications  |  design: docs-site (Schema Diagram)
public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    [StringLength(40)]
    public string Type { get; set; } = string.Empty;
    [StringLength(150)]
    public string Title { get; set; } = string.Empty;
    [StringLength(500)]
    public string Message { get; set; } = string.Empty;
    [StringLength(300)]
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
