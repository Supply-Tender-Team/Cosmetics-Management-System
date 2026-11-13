using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Bids;

/// <summary>One supplier offer for one tender.</summary>
// OWNER: Sara  |  table: Bids  |  design: docs-site (Schema Diagram)
public class Bid
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int SupplierProfileId { get; set; }
    /// <summary>Allowed values: Draft, Submitted, Under Review, Compliant, Non-Compliant, Accepted, Rejected, Withdrawn.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Draft";
    [Precision(18, 2)]
    public decimal TotalPrice { get; set; }
    [StringLength(3)]
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    [StringLength(300)]
    public string PaymentTerms { get; set; } = string.Empty;
    public string TechnicalSummary { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public SupplierProfile SupplierProfile { get; set; } = null!;
    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();
    public ICollection<BidDocument> BidDocuments { get; set; } = new List<BidDocument>();
    public ICollection<ComplianceCheck> ComplianceChecks { get; set; } = new List<ComplianceCheck>();
    public ICollection<TechnicalEvaluation> TechnicalEvaluations { get; set; } = new List<TechnicalEvaluation>();
    public ICollection<FinancialEvaluation> FinancialEvaluations { get; set; } = new List<FinancialEvaluation>();
    public ICollection<AiBidAnalysis> AiBidAnalyses { get; set; } = new List<AiBidAnalysis>();
    public ICollection<EvaluationReport> EvaluationReports { get; set; } = new List<EvaluationReport>();
    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();
    public ICollection<TopCommitteeVote> TopCommitteeVotes { get; set; } = new List<TopCommitteeVote>();
    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();
}
