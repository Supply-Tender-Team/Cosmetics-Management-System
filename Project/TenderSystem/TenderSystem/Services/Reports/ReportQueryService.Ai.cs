using System.Globalization;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    public async Task<AiReportFacts?> ReadAiFactsAsync(ClaimsPrincipal user, AiReportRequestViewModel input, CancellationToken ct)
    {
        if (!user.IsInRole(Roles.Admin) || user.Identity?.IsAuthenticated != true) return null;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return null;
        // Keep the short database read consistent; release all locks before the model is called.
        await using var transaction = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        // Resolve selections again from this report's current authorized database choices.
        var page = await ReadAsync(user, new ReportFilterViewModel { Type = input.Type }, false, ct);
        if (page?.Selected == null || page.Error != null) return null;
        var option = page.Selected;
        var filter = new ReportFilterViewModel { Type = option.Key };
        var scope = "All available records";
        if (input.SpecificTopic)
        {
            var choices = input.Focus switch
            {
                "supplier" when option.HasSupplier => page.Suppliers,
                "department" when option.HasDepartment => page.Departments,
                "person" when option.HasPerson => page.People,
                "tender" when option.HasTender => page.Tenders,
                _ => null
            };
            if (choices != null)
            {
                var chosen = choices.SingleOrDefault(c => c.Value == input.FocusId);
                if (chosen == null) throw new ArgumentException("Choose an available topic from this report.");
                scope = input.Focus + ": " + chosen.Text;
                // People dropdowns include emails for disambiguation; scope only needs the stored name.
                if (input.Focus == "person") scope = "Person: " + chosen.Text.Split(" — ")[0];
                if (input.Focus == "supplier") filter.SupplierId = int.Parse(chosen.Value!, CultureInfo.InvariantCulture);
                if (input.Focus == "department") filter.DepartmentId = int.Parse(chosen.Value!, CultureInfo.InvariantCulture);
                if (input.Focus == "person") filter.PersonId = chosen.Value;
                if (input.Focus == "tender") filter.TenderNumber = chosen.Value;
            }
            else if (input.Focus == "status" && page.Statuses.Contains(input.FocusId ?? ""))
            { filter.Status = input.FocusId; scope = "Status: " + input.FocusId; }
            else if (input.Focus == "compare-suppliers" && option.HasSupplier) scope = "Compare suppliers";
            else if (input.Focus == "compare-departments" && option.HasDepartment) scope = "Compare departments";
            else if (input.Focus == "period" && option.HasDate && (input.From.HasValue || input.To.HasValue)) scope = "Date range";
            else throw new ArgumentException("Choose a supported focus for this report.");
            if ((input.From.HasValue || input.To.HasValue) && !option.HasDate)
                throw new ArgumentException("This report does not support date filtering.");
            if (input.From > input.To || input.To?.Year >= 9999)
                throw new ArgumentException("Choose a valid date range.");
            filter.From = input.From?.Date; filter.To = input.To?.Date;
            if (filter.From.HasValue || filter.To.HasValue)
                scope += "; " + option.DateLabel + " dates: " + (filter.From?.ToString("yyyy-MM-dd") ?? "any start")
                    + " to " + (filter.To?.ToString("yyyy-MM-dd") ?? "any end") + " UTC";
        }
        // Never import the ordinary Results filters or its visible-page limit.
        var query = CreateQuery(option.Key, userId, filter);
        if (option.Key.StartsWith("admin-", StringComparison.Ordinal))
            query = ApplySimpleAdminFilters(query, new ReportPageViewModel { Selected = option, Filter = filter });
        else
        {
            if (filter.Status != null) query = query.Where(x => x.Status == filter.Status);
            if (filter.SupplierId.HasValue) query = query.Where(x => x.SupplierId == filter.SupplierId);
            if (filter.DepartmentId.HasValue) query = query.Where(x => x.DepartmentId == filter.DepartmentId);
            if (filter.PersonId != null) query = query.Where(x => x.PersonId == filter.PersonId);
            if (filter.TenderNumber != null) query = query.Where(x => x.TenderNumber == filter.TenderNumber);
            if (filter.From.HasValue) query = query.Where(x => x.OccurredAt >= filter.From.Value);
            if (filter.To.HasValue) { var until = filter.To.Value.AddDays(1); query = query.Where(x => x.OccurredAt < until); }
        }
        var count = await query.CountAsync(ct);
        var figures = new List<AiReportFigure> { new("Matching records", count.ToString(CultureInfo.InvariantCulture)) };
        var limits = new List<string>();
        var facts = new StringBuilder();
        void Append(string line, string omitted)
        {
            if (facts.Length + line.Length + Environment.NewLine.Length <= AiReportService.MaxFactsChars) facts.AppendLine(line);
            else if (!limits.Contains(omitted)) limits.Add(omitted);
        }
        static string Text(string? value, int max = 180) => new(Regex.Replace(value ?? "Not available", "<[^>]*>", " ").Replace('\r', ' ').Replace('\n', ' ').Take(max).ToArray());
        Append("Full-scope record count: " + count, "Some aggregate labels were omitted due to the facts size limit.");
        var statuses = await query.GroupBy(x => x.Status).Select(g => new { Label = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.Label).ToListAsync(ct);
        foreach (var status in statuses.Take(40))
        {
            Append("Status " + Text(status.Label) + ": " + status.Count, "Some status groups were omitted due to the facts size limit.");
            if (figures.Count < 5 && status.Label.Length <= 80) figures.Add(new(status.Label, status.Count.ToString(CultureInfo.InvariantCulture)));
        }
        if (statuses.Count > 40) limits.Add("Only the 40 largest status groups are listed; the record count covers the full scope.");
        if (option.HasAmount)
        {
            var amounts = await query.Where(x => x.Amount != null).GroupBy(x => x.Currency)
                .Select(g => new { Currency = g.Key, Total = g.Sum(x => x.Amount!.Value), Count = g.Count() }).OrderBy(x => x.Currency).ToListAsync(ct);
            foreach (var amount in amounts.Take(20))
            {
                var value = amount.Total.ToString("0.00", CultureInfo.InvariantCulture) + " " + Text(amount.Currency, 20);
                Append("Amount total " + value + "; records with amounts: " + amount.Count, "Some currency totals were omitted due to the facts size limit.");
                if (figures.Count < 9) figures.Add(new("Total " + Text(amount.Currency, 20), value));
            }
            var missing = count - amounts.Sum(x => x.Count);
            Append("Records without a recorded amount: " + missing, "Amount coverage detail was omitted.");
            if (amounts.Count > 20) limits.Add("Only the first 20 currency totals are listed. Different currencies have not been combined.");
        }
        if (option.Key == "admin-supplier-activity")
        {
            var pairs = await query.GroupBy(x => 1).Select(g => new { Invited = g.Sum(x => x.Count), Participated = g.Sum(x => x.SecondCount), Awarded = g.Sum(x => x.ReadyCount) }).SingleOrDefaultAsync(ct);
            if (pairs != null) Append($"Supplier-tender pairs: invited {pairs.Invited}; participated {pairs.Participated}; awarded {pairs.Awarded}. These are pairs, not distinct tenders across all suppliers.", "Supplier activity totals were omitted.");
        }
        if (option.HasDepartment || input.Focus == "compare-departments")
        {
            var groups = await query.GroupBy(x => x.Department).Select(g => new { Label = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.Label).Take(21).ToListAsync(ct);
            foreach (var group in groups.Take(20)) Append("Department " + Text(group.Label) + ": " + group.Count, "Some department groups were omitted due to the facts size limit.");
            if (groups.Count > 20) limits.Add("Only the 20 largest department groups are listed; totals cover the full scope.");
        }
        if (option.Key == "admin-tenders")
        {
            var tenderIds = query.Select(x => x.TenderId);
            var related = _db.Invitations.Where(x => tenderIds.Contains(x.TenderId)).Select(x => new { x.SupplierProfileId, x.TenderId })
                .Union(_db.Bids.Where(x => tenderIds.Contains(x.TenderId)).Select(x => new { x.SupplierProfileId, x.TenderId }))
                .Union(_db.TenderSuppliers.Where(x => tenderIds.Contains(x.TenderId)).Select(x => new { x.SupplierProfileId, x.TenderId }));
            var groups = await related.Join(_db.SupplierProfiles, x => x.SupplierProfileId, x => x.Id, (link, supplier) => new { SupplierId = supplier.Id, supplier.CompanyName, link.TenderId })
                .GroupBy(x => new { x.SupplierId, x.CompanyName }).Select(g => new { Label = g.Key.CompanyName + " (supplier " + g.Key.SupplierId + ")", Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.Label).Take(21).ToListAsync(ct);
            foreach (var group in groups.Take(20)) Append("Related supplier-tender pairs for " + Text(group.Label) + ": " + group.Count, "Some related supplier groups were omitted due to the facts size limit.");
            if (groups.Count > 20) limits.Add("Only the 20 largest related supplier groups are listed; the tender count covers the full scope.");
        }
        else if (option.HasSupplier || input.Focus == "compare-suppliers")
        {
            var groups = await query.Where(x => x.SupplierId != null).GroupBy(x => new { x.SupplierId, x.Supplier }).Select(g => new { Label = g.Key.Supplier + " (supplier " + g.Key.SupplierId + ")", Count = g.Count() }).OrderByDescending(x => x.Count).ThenBy(x => x.Label).Take(21).ToListAsync(ct);
            foreach (var group in groups.Take(20)) Append("Supplier " + Text(group.Label) + ": " + group.Count, "Some supplier groups were omitted due to the facts size limit.");
            if (groups.Count > 20) limits.Add("Only the 20 largest supplier groups are listed; totals cover the full scope.");
        }
        var rows = await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Reference).ThenBy(x => x.RecordId).Take(40).ToListAsync(ct);
        if (option.Key.StartsWith("admin-", StringComparison.Ordinal)) await FillAdminBusinessDetailsAsync(option.Key, userId, rows, ct);
        else await FillRecordDetailsAsync(option.Key, userId, rows, ct);
        var included = 0;
        foreach (var row in rows)
        {
            var line = "Record " + Text(row.Reference) + "; status " + Text(row.Status) + "; tender " + Text(row.TenderNumber)
                + "; department " + Text(row.Department) + "; supplier " + Text(row.Supplier)
                + "; date " + (row.OccurredAt?.ToString("yyyy-MM-dd") ?? "not available");
            if (option.HasAmount) line += "; amount " + (row.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "not available") + " " + Text(row.Currency, 20);
            if (option.Key == "admin-supplier-activity") line += $"; invited {row.Count}; participated {row.SecondCount}; awarded {row.ReadyCount}";
            foreach (var detail in row.MoreDetails.Take(8)) line += "; " + Text(detail.Label, 80) + ": " + Text(detail.Value, 220);
            if (facts.Length + line.Length + Environment.NewLine.Length > AiReportService.MaxFactsChars) break;
            facts.AppendLine(line); included++;
        }
        if (included < count) limits.Add($"Supporting records shown: {included} of {count}, newest first. Record counts and included aggregate totals cover the full selected scope.");
        limits.Add("Supporting record labels and detail text may be shortened; at most 8 detail fields per supporting record are included. Attachments and raw audit payloads are not analyzed.");
        if (option.Key is "admin-requests" or "admin-tenders" or "admin-no-offers")
            limits.Add("Approval-time and evaluation-delay metrics are not calculated by this report. Missing deadlines or timing definitions are not inferred.");
        if (option.Key is "admin-documents" or "suppliers") limits.Add("Document expiry and supplier approval status do not prove that each legal document has been verified.");
        if (option.Key is "admin-awards" or "admin-supplier-activity") limits.Add("Awards are not final contracts or evidence of delivery performance.");
        if (scope.Length > 400) { scope = scope[..397] + "..."; limits.Add("The scope display label was shortened; the database selection used the full validated identifier."); }
        if (transaction != null) await transaction.CommitAsync(ct);
        return new AiReportFacts(option.Title, scope, count, figures, facts.ToString(), limits);
    }
}
