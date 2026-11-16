using System.Globalization;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

public class ReportTable
{
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();

    public static ReportTable Create(ReportPageViewModel page, bool includeDetails = false)
    {
        var table = new ReportTable();
        if (page.Selected == null) return table;
        table.Headers.AddRange(new[] { "Record", page.Selected.DetailLabel, "Status" });
        if (page.Selected.HasDate) table.Headers.Add(page.Selected.DateLabel + " (UTC)");
        if (page.Selected.HasProgress) table.Headers.Add("Ready forms");
        if (page.Selected.HasAmount) table.Headers.Add(page.Selected.AmountLabel);
        if (page.Selected.HasAmount && page.Selected.HasCurrency) table.Headers.Add("Currency");
        if (includeDetails) table.Headers.Add("Additional details");
        foreach (var row in page.Rows)
        {
            var reference = row.Reference;
            if (!string.IsNullOrEmpty(row.Title) && row.Title != row.Reference) reference += "\n" + row.Title;
            if (!string.IsNullOrEmpty(row.TenderNumber) && row.TenderNumber != row.Reference) reference += "\n" + row.TenderNumber;
            var detail = row.Detail;
            if (!string.IsNullOrEmpty(row.Department)) detail += "\n" + row.Department;
            if (!string.IsNullOrEmpty(row.Supplier) && row.Supplier != row.Title && row.Supplier != row.Reference) detail += "\n" + row.Supplier;
            var cells = new List<string> { reference, detail.Trim(), row.Status };
            if (page.Selected.HasDate) cells.Add(row.OccurredAt?.ToString("dd MMM yyyy") ?? "Not recorded");
            if (page.Selected.HasProgress) cells.Add(row.RequiredCount > 0 ? row.ReadyCount + " / " + row.RequiredCount : "No eligible offers");
            if (page.Selected.HasAmount) { cells.Add(row.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "Pending"); if (page.Selected.HasCurrency) cells.Add(row.Currency); }
            if (includeDetails) cells.Add(string.Join("\n", row.MoreDetails.Select(f => f.Label + ": " + f.Value)));
            table.Rows.Add(cells);
        }
        return table;
    }
}
