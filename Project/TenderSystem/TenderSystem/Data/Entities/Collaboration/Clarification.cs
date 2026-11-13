using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Collaboration;

/// <summary>A question between two allowed roles, linked to a request, tender or bid.</summary>
// OWNER: Dev 7  |  table: Clarifications  |  design: docs-site (Schema Diagram)
public class Clarification
{
    public int Id { get; set; }
    [StringLength(200)]
    public string Subject { get; set; } = string.Empty;
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;
    /// <summary>Allowed values: Pending, Answered, Resolved.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Pending";
    public string SenderUserId { get; set; } = string.Empty;
    public string ReceiverUserId { get; set; } = string.Empty;
    public int? RequisitionId { get; set; }
    public int? TenderId { get; set; }
    public int? BidId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    // Navigation properties
    public ApplicationUser SenderUser { get; set; } = null!;
    public ApplicationUser ReceiverUser { get; set; } = null!;
    public PurchaseRequisition? Requisition { get; set; }
    public Tender? Tender { get; set; }
    public Bid? Bid { get; set; }
    public ICollection<ClarificationMessage> ClarificationMessages { get; set; } = new List<ClarificationMessage>();
}
