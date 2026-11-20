using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Requests;

/// <summary>A purchase request created by a department.</summary>
// OWNER: Dana  |  table: PurchaseRequisitions  |  design: docs-site (Schema Diagram)
public class PurchaseRequisition
{
    public int Id { get; set; }
    [StringLength(30)]
    public string RequestNumber { get; set; } = string.Empty;
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
    /// <summary>Allowed values: Low, Medium, High, Urgent.</summary>
    [StringLength(10)]
    public string Priority { get; set; } = string.Empty;
    [Column(TypeName = "date")]
    public DateTime TargetDate { get; set; }
    /// <summary>Allowed values: Draft, Submitted, Under Review, Approved, Rejected, Returned, Converted to Tender.</summary>
    [StringLength(30)]
    public string Status { get; set; } = "Draft";
    public int DepartmentId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string? ReviewedByUserId { get; set; }
    [StringLength(1000)]
    public string? ReviewNote { get; set; }
    [Precision(18, 2)]
    public decimal? EstimatedBudget { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    // Evaluation Preferences, written by the requester. Both weights are percentages (0 to 100) and must total 100 when the request is submitted.
    // Null for old requests and for drafts that are still incomplete.
    [Precision(5, 2)]
    public decimal? TechnicalEvaluationWeight { get; set; }
    [Precision(5, 2)]
    public decimal? FinancialEvaluationWeight { get; set; }
    /// <summary>What matters most in the evaluation (quality, delivery time, supplier experience, price, sustainability...).</summary>
    [StringLength(2000)]
    public string? EvaluationFocus { get; set; }

    // Navigation properties
    public Department Department { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
    public ICollection<PurchaseRequisitionItem> PurchaseRequisitionItems { get; set; } = new List<PurchaseRequisitionItem>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();
}
