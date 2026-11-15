using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Evaluation.Models;

public class SubmitReportViewModel
{
    public int TenderId { get; set; }
    [Required, Range(1, int.MaxValue)]
    public int? RecommendedBidId { get; set; }
    [Required, StringLength(4000, MinimumLength = 20)]
    public string Justification { get; set; } = string.Empty;
}
