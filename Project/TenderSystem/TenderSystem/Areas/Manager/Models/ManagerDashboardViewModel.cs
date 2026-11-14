namespace TenderSystem.Areas.Manager.Models;

public class ManagerDashboardViewModel
{
    public int PendingReportCount { get; set; }
    public int DecisionsThisMonth { get; set; }
    public int AwaitingTopCommitteeCount { get; set; }
    public List<ManagerPendingReportViewModel> PendingReports { get; set; } = [];
    public List<ManagerDecisionHistoryViewModel> RecentDecisions { get; set; } = [];
    public List<ManagerDecisionHistoryViewModel> TopCommitteeRejections { get; set; } = [];
}
