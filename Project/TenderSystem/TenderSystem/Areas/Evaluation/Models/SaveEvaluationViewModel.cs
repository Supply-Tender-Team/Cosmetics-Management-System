using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Evaluation.Models;

public class SaveEvaluationViewModel
{
    public int BidId { get; set; }
    public List<EvaluationScoreViewModel> Scores { get; set; } = new List<EvaluationScoreViewModel>();
    [StringLength(1000)]
    public string? FinancialNotes { get; set; }
}
