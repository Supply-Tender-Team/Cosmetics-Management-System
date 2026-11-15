namespace TenderSystem.Areas.Manager.Models;

public class ManagerPendingReportViewModel
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public decimal EstimatedValue { get; set; }
    public bool RequiresTopCommittee { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string TechnicalSummary { get; set; } = string.Empty;
    public string FinancialSummary { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
}
