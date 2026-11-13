using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>The ONE unified report with the committee recommendation.</summary>
// OWNER: Dev 6  |  table: EvaluationReports  |  design: docs-site (Schema Diagram)
public class EvaluationReport
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    /// <summary>Allowed values: Recommendation Pending, Manager Approved, Manager Rejected, Returned for Review, Committee Approved, Committee Rejected.</summary>
    [StringLength(30)]
    public string Status { get; set; } = "Recommendation Pending";
    public int? RecommendedBidId { get; set; }
    public string TechnicalSummary { get; set; } = string.Empty;
    public string FinancialSummary { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
    public string PreparedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public Bid? RecommendedBid { get; set; }
    public ApplicationUser PreparedByUser { get; set; } = null!;
    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();
}
