namespace TenderSystem.Areas.Manager.Models;

public class ManagerDecisionHistoryViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime DecidedAt { get; set; }
    public bool HasTopCommitteeResult { get; set; }
    public bool OfficerResultSent { get; set; }
}
