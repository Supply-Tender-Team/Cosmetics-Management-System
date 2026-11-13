using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>Financial analysis of a bid (total cost, taxes, logistics).</summary>
// OWNER: Dev 6  |  table: FinancialEvaluations  |  design: docs-site (Schema Diagram)
public class FinancialEvaluation
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public string EvaluatorUserId { get; set; } = string.Empty;
    [Precision(5, 2)]
    public decimal Score { get; set; }
    [Precision(18, 2)]
    public decimal TotalCost { get; set; }
    [Precision(18, 2)]
    public decimal TaxesAndFees { get; set; }
    [Precision(18, 2)]
    public decimal LogisticsCost { get; set; }
    [StringLength(1000)]
    public string? Notes { get; set; }
    public DateTime EvaluatedAt { get; set; }

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public ApplicationUser EvaluatorUser { get; set; } = null!;
}
