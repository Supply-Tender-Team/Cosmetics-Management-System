using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private static async Task LoadBreakdownAsync(ReportPageViewModel model, IQueryable<ReportRecordViewModel> query, CancellationToken ct)
    {
        if (!model.Selected!.Key.StartsWith("admin-", StringComparison.Ordinal) && !model.Selected.Key.StartsWith("committee-", StringComparison.Ordinal)) return;
        // A small automatic status summary; the user does not have to configure grouping.
        model.Breakdown = await query.GroupBy(r => r.Status).OrderBy(g => g.Key)
            .Select(g => new ReportFieldViewModel { Label = g.Key, Value = g.Count().ToString() }).ToListAsync(ct);
    }
}
