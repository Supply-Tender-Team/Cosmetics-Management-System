using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationScoreViewModel
{
    public int CriterionId { get; set; }
    [ValidateNever]
    public string Name { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public int MaxScore { get; set; }
    [Required(ErrorMessage = "Enter a score for this criterion.")]
    [Range(typeof(decimal), "0", "999.99")]
    public decimal? Score { get; set; }
    [StringLength(1000)]
    public string? Notes { get; set; }
}
