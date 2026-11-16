namespace TenderSystem.Areas.Manager.Models;

public sealed class ManagerTopCommitteeResultViewModel
{
    public int TenderId { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTime DecidedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public decimal? TotalPrice { get; set; }
    public string? Currency { get; set; }
    public bool OfficerNotified { get; set; }
}
