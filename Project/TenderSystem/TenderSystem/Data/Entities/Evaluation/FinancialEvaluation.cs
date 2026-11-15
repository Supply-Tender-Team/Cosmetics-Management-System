using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>Financial analysis of a bid (total cost, taxes, logistics).</summary>
// OWNER: Marwa  |  table: FinancialEvaluations  |  design: docs-site (Schema Diagram)
// One row per member and offer (unique index below). This row also carries the member's final confirmation (ReadyAt).
[Index(nameof(BidId), nameof(EvaluatorUserId), IsUnique = true)]
public class FinancialEvaluation
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public string EvaluatorUserId { get; set; } = string.Empty;
    /// <summary>NULL = the comparable financial points are not available yet. Server-calculated, never zero by default.</summary>
    [Precision(5, 2)]
    public decimal? Score { get; set; }
    [Precision(18, 2)]
    public decimal TotalCost { get; set; }
    [Precision(18, 2)]
    public decimal TaxesAndFees { get; set; }
    [Precision(18, 2)]
    public decimal LogisticsCost { get; set; }
    [StringLength(1000)]
    public string? Notes { get; set; }
    /// <summary>Last time the form was saved.</summary>
    public DateTime EvaluatedAt { get; set; }
    /// <summary>When the member confirmed the combined technical and financial form as final (Ready). NULL = still a draft, whether empty or full. Once set, the form is locked.</summary>
    public DateTime? ReadyAt { get; set; }

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public ApplicationUser EvaluatorUser { get; set; } = null!;
}
