namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationMemberViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsCurrentUser { get; set; }
    public bool IsSelected { get; set; }
    public decimal? TechnicalPoints { get; set; }
    public DateTime? ReadyAt { get; set; }
    public bool HasSavedScores { get; set; }
}
