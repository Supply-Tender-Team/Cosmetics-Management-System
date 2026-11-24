using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Governance;

/// <summary>
/// The evaluation deadline of one tender (RPT-01). It is written ONCE, when the tender is shared with the evaluation committee for the first time,
/// and never changes after that: sharing again does not reset it, a later change of the default days does not change it, rework keeps it.
/// The evaluation counts as finished when the committee report is submitted (EvaluationReport.SubmittedAt).
/// </summary>
// OWNER: Wadea  |  table: EvaluationSchedules
public class EvaluationSchedule
{
    public int Id { get; set; }

    /// <summary>One row per tender (unique in the database).</summary>
    public int TenderId { get; set; }

    /// <summary>When the tender was first shared with the committee, UTC.</summary>
    public DateTime EvaluationStartedAt { get; set; }

    /// <summary>The number of calendar days that was used, kept so the deadline can always be explained.</summary>
    public int SlaDays { get; set; }

    /// <summary>The deadline, UTC: EvaluationStartedAt plus SlaDays calendar days.</summary>
    public DateTime EvaluationDueAt { get; set; }

    // Navigation properties
    public Tender Tender { get; set; } = null!;
}
