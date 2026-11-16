namespace TenderSystem.Areas.Shared.Models.Reports;

public class ReportOptionViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool HasDate { get; set; } = true;
    public string DateLabel { get; set; } = "Created";
    public string DetailLabel { get; set; } = "Details";
    public string AmountLabel { get; set; } = "Amount";
    public bool HasCurrency { get; set; } = true;
    public bool HasAmount { get; set; }
    public bool HasDepartment { get; set; }
    public bool HasSupplier { get; set; }
    public bool HasTender { get; set; }
    public bool HasProgress { get; set; }
    public string? RelatedUrl { get; set; }
    public string? RelatedLabel { get; set; }
}
