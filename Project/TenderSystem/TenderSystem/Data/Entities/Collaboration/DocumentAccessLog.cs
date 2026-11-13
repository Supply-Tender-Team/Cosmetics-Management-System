using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Collaboration;

/// <summary>Optional: who viewed or downloaded a document (used by the watermark feature).</summary>
// OWNER: Dev 7  |  table: DocumentAccessLogs  |  design: docs-site (Schema Diagram)
public class DocumentAccessLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    [StringLength(40)]
    public string DocumentKind { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    /// <summary>Allowed values: View, Download.</summary>
    [StringLength(20)]
    public string Action { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
