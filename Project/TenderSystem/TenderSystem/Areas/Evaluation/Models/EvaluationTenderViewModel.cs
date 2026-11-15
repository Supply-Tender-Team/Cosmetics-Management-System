namespace TenderSystem.Areas.Evaluation.Models;

public class EvaluationTenderViewModel
{
    public int Id { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MemberRole { get; set; } = string.Empty;
    public DateTime SubmissionDeadline { get; set; }
}
