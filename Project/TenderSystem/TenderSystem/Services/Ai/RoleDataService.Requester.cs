using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// REQUESTER: only the requests this user created (RequestedByUserId == me), exactly like Areas/Requester/Controllers/RequestsController.cs.
// A requester never sees tenders, supplier offers or prices.
public sealed partial class RoleDataService
{
    private async Task<AiDataResult?> RequesterRecordAsync(string question, string userId, CancellationToken ct)
    {
        var number = RecordNumber(question);
        if (number != null)
        {
            var r = await _db.PurchaseRequisitions.AsNoTracking()
                .Where(x => x.RequestedByUserId == userId && x.RequestNumber == number)
                .Select(x => new
                {
                    x.RequestNumber, x.Title, x.Reason, x.Status, x.Priority, x.TargetDate, x.EstimatedBudget,
                    Department = x.Department.Name, x.ReviewNote, x.CreatedAt, x.SubmittedAt,
                    Items = x.PurchaseRequisitionItems.Select(i => new { i.ItemName, i.Quantity, i.Unit }).ToList(),
                })
                .FirstOrDefaultAsync(ct);
            if (r == null) return NotFound("RequestByNumber", "request");

            var facts = new StringBuilder()
                .AppendLine($"Request {r.RequestNumber}: {Cut(r.Title, 120)}")
                .AppendLine($"Status: {r.Status}. Priority: {r.Priority}. Department: {r.Department}.")
                .AppendLine($"Created: {Day(r.CreatedAt)}. Submitted: {Day(r.SubmittedAt)}. Needed by: {Day(r.TargetDate)}.")
                .AppendLine($"Estimated budget: {(r.EstimatedBudget?.ToString("N2") ?? "not given")}.")
                .AppendLine($"Reason (written by the requester): {Cut(r.Reason, 300)}")
                .AppendLine($"Reviewer note: {(string.IsNullOrWhiteSpace(r.ReviewNote) ? "none" : Cut(r.ReviewNote, 300))}")
                .ToString();
            return Make("RequestByNumber", new AiTable($"Items of request {r.RequestNumber}", ["Item", "Quantity", "Unit"],
                r.Items.Select(i => new[] { Cut(i.ItemName, 80), i.Quantity.ToString("0.##"), i.Unit }).ToList()), facts);
        }

        return null;
    }
}
