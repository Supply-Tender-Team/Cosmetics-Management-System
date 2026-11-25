namespace TenderSystem.Services.Reports;

public static class ReportTimingRules
{
    public static string EvaluationStatus(DateTime? start, DateTime? due, DateTime? completed, DateTime now)
    {
        if (!start.HasValue || !due.HasValue || due < start || start > now || completed < start || completed > now) return "Unknown";
        return completed.HasValue ? completed > due ? "Completed late" : "Completed on time" : now > due ? "Overdue" : "In progress";
    }
    public static decimal? DelayHours(DateTime? start, DateTime? due, DateTime? completed, DateTime now)
    {
        if (EvaluationStatus(start, due, completed, now) == "Unknown") return null;
        return Math.Max(0, (decimal)((completed ?? now) - due!.Value).TotalHours);
    }
    public static (DateTime? SubmittedAt, DateTime? ApprovedAt, decimal? Hours) ApprovalTiming(
        IEnumerable<TenderSystem.Data.Entities.Collaboration.AuditLog> source, DateTime now)
    {
        var events = source.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToList();
        var approved = events.FirstOrDefault(a => a.Action == "Approved");
        var submitted = events.FirstOrDefault(a => a.Action == "Submitted");
        if (approved == null || submitted == null || approved.CreatedAt > now || submitted.CreatedAt > approved.CreatedAt
            || (submitted.CreatedAt == approved.CreatedAt && submitted.Id >= approved.Id))
            return (submitted?.CreatedAt, approved?.CreatedAt, null);
        return (submitted.CreatedAt, approved.CreatedAt, (decimal)(approved.CreatedAt - submitted.CreatedAt).TotalHours);
    }
    public static decimal? RepeatRate(int repeating, int awarded) => awarded == 0 ? null : 100m * repeating / awarded;
}
