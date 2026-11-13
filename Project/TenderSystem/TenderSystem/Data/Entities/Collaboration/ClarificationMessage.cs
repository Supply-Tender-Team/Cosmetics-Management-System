using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Collaboration;

/// <summary>The replies and follow-ups inside a clarification thread.</summary>
// OWNER: Deyaa  |  table: ClarificationMessages  |  design: docs-site (Schema Diagram)
public class ClarificationMessage
{
    public int Id { get; set; }
    public int ClarificationId { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    [StringLength(2000)]
    public string Body { get; set; } = string.Empty;
    [StringLength(300)]
    public string? AttachmentPath { get; set; }
    public DateTime SentAt { get; set; }

    // Navigation properties
    public Clarification Clarification { get; set; } = null!;
    public ApplicationUser SenderUser { get; set; } = null!;
}
