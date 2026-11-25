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
        var delivery = page.Selected.Key == "admin-award-notices";
        var tenderCounts = page.Selected.Key is "admin-tenders" or "admin-no-offers";
        var supplierCounts = page.Selected.Key == "admin-supplier-activity";
        var supplierOffers = page.Selected.Key is "admin-offers" or "admin-excluded-offers";
        if (delivery) table.Headers.AddRange(new[] { "Email", "Tender", "Supplier", "Queue status", "Delivery mode", "Queued (UTC)", "Recorded Sent (UTC)" });
        else if (tenderCounts) table.Headers.AddRange(new[] { "Tender", "Invited suppliers", "Submitted offers", "Status", "Created (UTC)" });
        else if (supplierCounts) table.Headers.AddRange(new[] { "Supplier", "Invited tenders", "Tenders with offers", "Won tenders", "Status" });
        else if (supplierOffers) table.Headers.AddRange(new[] { "Offer", "Supplier", "Tender", "Status", "Submitted (UTC)", page.Selected.AmountLabel, "Currency" });
        else table.Headers.AddRange(new[] { page.Selected.RecordLabel, page.Selected.DetailLabel, "Status" });
        if (!delivery && !tenderCounts && !supplierCounts && !supplierOffers)
        {
        if (page.Selected.HasDate) table.Headers.Add(page.Selected.DateLabel + " (UTC)");
        if (page.Selected.HasProgress) table.Headers.Add("Ready forms");
        if (page.Selected.HasAmount) table.Headers.Add(page.Selected.AmountLabel);
        if (page.Selected.HasAmount && page.Selected.HasCurrency) table.Headers.Add("Currency");
        }
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
            if (delivery) cells = new() { row.Reference + " / " + row.Title, row.TenderNumber, row.Supplier, row.Status, row.DeliveryMode, row.OccurredAt?.ToString("dd MMM yyyy HH:mm") ?? "Not recorded", row.ThirdDate?.ToString("dd MMM yyyy HH:mm") ?? "Not recorded" };
            else if (tenderCounts) cells = new() { reference, row.Count.ToString(), row.SecondCount.ToString(), row.Status, row.OccurredAt?.ToString("dd MMM yyyy") ?? "Not recorded" };
            else if (supplierCounts) cells = new() { row.Supplier, row.Count.ToString(), row.SecondCount.ToString(), row.ReadyCount.ToString(), row.Status };
            else if (supplierOffers) cells = new() { row.Reference, row.Supplier, row.TenderNumber, row.Status, row.OccurredAt?.ToString("dd MMM yyyy") ?? "Not submitted", row.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "Pending", row.Currency };
            else
            {
            if (page.Selected.HasDate) cells.Add(row.OccurredAt?.ToString("dd MMM yyyy") ?? "Not recorded");
            if (page.Selected.HasProgress) cells.Add(row.RequiredCount > 0 ? row.ReadyCount + " / " + row.RequiredCount : "No eligible offers");
            if (page.Selected.HasAmount) { cells.Add(row.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "Pending"); if (page.Selected.HasCurrency) cells.Add(row.Currency); }
            }
            if (includeDetails) cells.Add(string.Join("\n", row.MoreDetails.Select(f => f.Label + ": " + f.Value)));
            table.Rows.Add(cells);
        }
        return table;
    }
}
