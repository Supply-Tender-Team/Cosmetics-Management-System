using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationScoreViewModel
{
    [ValidateNever]
    public string Type { get; set; } = string.Empty;
    public int CriterionId { get; set; }
    [ValidateNever]
    public string Name { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public int MaxScore { get; set; }
    [Range(typeof(decimal), "0", "999.99")]
    public decimal? Score { get; set; }
    [StringLength(1000)]
    public string? Notes { get; set; }
}
