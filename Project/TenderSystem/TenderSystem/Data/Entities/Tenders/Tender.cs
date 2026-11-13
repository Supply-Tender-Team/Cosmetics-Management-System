using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;

namespace TenderSystem.Data.Entities.Tenders;

/// <summary>A tender created from an approved request.</summary>
// OWNER: Dev 4  |  table: Tenders  |  design: docs-site (Schema Diagram)
public class Tender
{
    public int Id { get; set; }
    [StringLength(30)]
    public string TenderNumber { get; set; } = string.Empty;
    public int RequisitionId { get; set; }
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Terms { get; set; } = string.Empty;
    /// <summary>Allowed values: Draft, Published, Open, Closed, Under Evaluation, Awaiting Manager Review, Awaiting Committee Decision, Awarded, Cancelled.</summary>
    [StringLength(40)]
    public string Status { get; set; } = "Draft";
    public DateTime? PublishDate { get; set; }
    public DateTime SubmissionDeadline { get; set; }
    [Precision(18, 2)]
    public decimal EstimatedValue { get; set; }
    public bool RequiresTopCommittee { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public int? AwardedSupplierId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public PurchaseRequisition Requisition { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public SupplierProfile? AwardedSupplier { get; set; }
    public ICollection<TenderSupplier> TenderSuppliers { get; set; } = new List<TenderSupplier>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public ICollection<EvaluationCriterion> EvaluationCriteria { get; set; } = new List<EvaluationCriterion>();
    public ICollection<CommitteeMember> CommitteeMembers { get; set; } = new List<CommitteeMember>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<AiBidAnalysis> AiBidAnalyses { get; set; } = new List<AiBidAnalysis>();
    public ICollection<EvaluationReport> EvaluationReports { get; set; } = new List<EvaluationReport>();
    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();
    public ICollection<TopCommitteeVote> TopCommitteeVotes { get; set; } = new List<TopCommitteeVote>();
    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();
}
