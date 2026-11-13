using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Evaluation;

namespace TenderSystem.Data.Entities.Tenders;

/// <summary>Scoring criteria and weights. Locked before bids are opened.</summary>
// OWNER: Nada  |  table: EvaluationCriteria  |  design: docs-site (Schema Diagram)
public class EvaluationCriterion
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;
    /// <summary>Allowed values: Technical, Financial.</summary>
    [StringLength(20)]
    public string Type { get; set; } = string.Empty;
    [Precision(5, 2)]
    public decimal WeightPercent { get; set; }
    public int MaxScore { get; set; }
    public bool IsLocked { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public ICollection<TechnicalEvaluation> TechnicalEvaluations { get; set; } = new List<TechnicalEvaluation>();
}
