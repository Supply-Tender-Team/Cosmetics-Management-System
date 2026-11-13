using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>Technical score per bid and per criterion.</summary>
// OWNER: Marwa  |  table: TechnicalEvaluations  |  design: docs-site (Schema Diagram)
public class TechnicalEvaluation
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public int CriterionId { get; set; }
    public string EvaluatorUserId { get; set; } = string.Empty;
    [Precision(5, 2)]
    public decimal Score { get; set; }
    [StringLength(1000)]
    public string? Notes { get; set; }
    public DateTime EvaluatedAt { get; set; }

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public EvaluationCriterion Criterion { get; set; } = null!;
    public ApplicationUser EvaluatorUser { get; set; } = null!;
}
