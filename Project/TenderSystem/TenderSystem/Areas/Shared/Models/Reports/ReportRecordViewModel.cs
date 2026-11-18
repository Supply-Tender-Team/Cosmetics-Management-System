namespace TenderSystem.Areas.Shared.Models.Reports;

public class ReportRecordViewModel
{
    public List<ReportFieldViewModel> MoreDetails { get; set; } = new();
    public string RecordId { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? OccurredAt { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string Department { get; set; } = string.Empty;
    public int? SupplierId { get; set; }
    public string Supplier { get; set; } = string.Empty;
    public string TenderNumber { get; set; } = string.Empty;
    public int? TenderId { get; set; }
    public int? RequestId { get; set; }
    public string? PersonId { get; set; }
    public string Person { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public string Compliance { get; set; } = string.Empty;
    public string DecisionLevel { get; set; } = string.Empty;
    public DateTime? SecondaryDate { get; set; }
    public DateTime? ThirdDate { get; set; }
    public int Count { get; set; }
    public int SecondCount { get; set; }
    public int ReadyCount { get; set; }
    public int RequiredCount { get; set; }
}
