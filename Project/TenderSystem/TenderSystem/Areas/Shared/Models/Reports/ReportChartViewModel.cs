namespace TenderSystem.Areas.Shared.Models.Reports;

public class ReportChartViewModel
{
    public string Title { get; set; } = "";
    public string FilterName { get; set; } = "";
    public List<ReportChartPointViewModel> Points { get; set; } = new();
}
