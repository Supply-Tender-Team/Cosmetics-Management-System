using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// SOURCING OFFICER: submitted requests (never other people's drafts), all tenders, and the submitted offers of a tender.
public sealed partial class AiDataService
{
    private async Task<AiDataResult?> SourcingAsync(string q, string question, CancellationToken ct)
    {
        var number = RecordNumber(question);
        if (number != null)
        {
            var tender = await _db.Tenders.AsNoTracking().Where(t => t.TenderNumber == number)
                .Select(t => new { t.Id, t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline, t.EstimatedValue }).FirstOrDefaultAsync(ct);
            if (tender != null)
            {
                var header = $"Tender {tender.TenderNumber}: {Cut(tender.Title, 120)}. Status: {tender.Status}. Submission deadline: {Time(tender.SubmissionDeadline)} UTC. Estimated value: {tender.EstimatedValue:N2}.";
                return await TenderOffersAsync(tender.Id, "TenderOffers", $"Offers of tender {tender.TenderNumber}", header, ct);
            }

            var r = await _db.PurchaseRequisitions.AsNoTracking()
                .Where(x => x.RequestNumber == number && x.Status != Statuses.RequestStatus.Draft)   // drafts are private to the requester
                .Select(x => new
                {
                    x.RequestNumber, x.Title, x.Reason, x.Status, x.Priority, x.TargetDate, x.EstimatedBudget,
                    Department = x.Department.Name, Requester = x.RequestedByUser.FullName,
                    Items = x.PurchaseRequisitionItems.Select(i => new { i.ItemName, i.Quantity, i.Unit }).ToList(),
                }).FirstOrDefaultAsync(ct);
            if (r == null) return NotFound("RecordByNumber", "request or tender");

            var facts = new StringBuilder()
                .AppendLine($"Request {r.RequestNumber}: {Cut(r.Title, 120)}")
                .AppendLine($"Status: {r.Status}. Priority: {r.Priority}. Department: {r.Department}. Requested by: {Cut(r.Requester, 40)}.")
                .AppendLine($"Needed by: {Day(r.TargetDate)}. Estimated budget: {(r.EstimatedBudget?.ToString("N2") ?? "not given")}.")
                .AppendLine($"Reason (written by the requester): {Cut(r.Reason, 300)}")
                .ToString();
            return Make("RecordByNumber", new AiTable($"Items of request {r.RequestNumber}", ["Item", "Quantity", "Unit"],
                r.Items.Select(i => new[] { Cut(i.ItemName, 80), i.Quantity.ToString("0.##"), i.Unit }).ToList()), facts);
        }

        if (Wants(q, "tender", "مناقص"))
        {
            var status = new[]
            {
                (Words: new[] { "draft", "مسود" }, Value: Statuses.TenderStatus.Draft),
                (Words: new[] { "published", "منشور" }, Value: Statuses.TenderStatus.Published),
                (Words: new[] { "open", "مفتوح" }, Value: Statuses.TenderStatus.Open),
                (Words: new[] { "closed", "مغلق" }, Value: Statuses.TenderStatus.Closed),
                (Words: new[] { "evaluation", "تقييم" }, Value: Statuses.TenderStatus.UnderEvaluation),
                (Words: new[] { "awarded", "مرسى" }, Value: Statuses.TenderStatus.Awarded),
                (Words: new[] { "cancelled", "canceled", "ملغ" }, Value: Statuses.TenderStatus.Cancelled),
            }.FirstOrDefault(s => Wants(q, s.Words)).Value;

            var query = _db.Tenders.AsNoTracking();
            if (status != null) query = query.Where(t => t.Status == status);
            var rows = await query.OrderByDescending(t => t.CreatedAt).Take(Count(question))
                .Select(t => new { t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline, Offers = t.Bids.Count(b => b.Status != Statuses.BidStatus.Draft) })
                .ToListAsync(ct);
            return Make("Tenders", new AiTable(status == null ? "Latest tenders" : $"Tenders with status {status}",
                ["Tender", "Title", "Status", "Submission deadline", "Offers"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), r.Status, Day(r.SubmissionDeadline), r.Offers.ToString() }).ToList()));
        }

        if (Wants(q, "request", "طلب"))
        {
            var rows = await _db.PurchaseRequisitions.AsNoTracking()
                .Where(x => x.Status == Statuses.RequestStatus.Submitted || x.Status == Statuses.RequestStatus.UnderReview)
                .OrderByDescending(x => x.SubmittedAt ?? x.CreatedAt).Take(Count(question))
                .Select(x => new { x.RequestNumber, x.Title, x.Status, x.Priority, Department = x.Department.Name, x.SubmittedAt })
                .ToListAsync(ct);
            return Make("IncomingRequests", new AiTable("Requests waiting for sourcing review", ["Number", "Title", "Department", "Priority", "Status", "Submitted"],
                rows.Select(r => new[] { r.RequestNumber, Cut(r.Title, 60), r.Department, r.Priority, r.Status, Day(r.SubmittedAt) }).ToList()));
        }

        return null;
    }
}
