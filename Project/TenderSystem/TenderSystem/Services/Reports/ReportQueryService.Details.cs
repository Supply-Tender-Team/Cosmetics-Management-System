using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    // IDs come only from CreateQuery's already-authorized result page, never directly from the browser.
    private async Task FillRecordDetailsAsync(string type, string userId, List<ReportRecordViewModel> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => int.TryParse(r.RecordId, out var id) ? id : 0).Where(id => id > 0).ToList();
        if (type is "users" or "suppliers" or "departments" or "audit" or "sign-ins")
        { await FillAdminDetailsAsync(type, rows, ids, ct); return; }

        if (type is "my-requests" or "received-requests")
        {
            var query = _db.PurchaseRequisitions.AsNoTracking().Where(r => ids.Contains(r.Id));
            query = type == "my-requests" ? query.Where(r => r.RequestedByUserId == userId)
                : query.Where(r => r.Status != Statuses.RequestStatus.Draft && r.SubmittedAt != null);
            var requests = await query.Select(r => new { r.Id, r.Reason, r.TargetDate, r.SubmittedAt,
                RequestedBy = r.RequestedByUser.FullName, r.ReviewNote,
                ItemCount = r.PurchaseRequisitionItems.Count(), Items = r.PurchaseRequisitionItems.OrderBy(i => i.Id).Take(3)
                    .Select(i => new { i.ItemName, i.Quantity, i.Unit }).ToList() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var request = requests.Single(r => r.Id.ToString() == row.RecordId);
                AddDetail(row, "Requested by", request.RequestedBy);
                AddDetail(row, "Needed by", request.TargetDate.ToString("dd MMM yyyy"));
                AddDetail(row, "Submitted", DateText(request.SubmittedAt));
                var items = string.Join("; ", request.Items.Select(i => $"{i.Quantity:0.##} {i.Unit} — {i.ItemName}"));
                if (request.ItemCount > request.Items.Count) items += $"; plus {request.ItemCount - request.Items.Count} more item(s)";
                AddDetail(row, "Requested items", items);
                AddDetail(row, "Purpose", Excerpt(request.Reason));
                AddDetail(row, "Review note", Excerpt(request.ReviewNote));
            }
        }
        else if (type is "tenders" or "evaluation" or "assigned-evaluation" or "awards")
        {
            var tenders = await _db.Tenders.AsNoTracking().Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Requisition.RequestNumber, t.SubmissionDeadline,
                    Invited = t.Invitations.Count(), Submitted = t.Bids.Count(b => b.Status != Statuses.BidStatus.Draft && b.SubmittedAt != null),
                    Eligible = t.Bids.Count(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted),
                    Members = t.CommitteeMembers.Count(), Report = t.EvaluationReports.OrderByDescending(r => r.CreatedAt)
                        .Select(r => new { r.Status, r.SubmittedAt, PreparedBy = r.PreparedByUser.FullName }).FirstOrDefault() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var tender = tenders.Single(t => t.Id.ToString() == row.RecordId);
                AddDetail(row, "Purchase request", tender.RequestNumber);
                AddDetail(row, "Offer deadline", DateText(tender.SubmissionDeadline));
                AddDetail(row, "Supplier invitations", tender.Invited.ToString());
                AddDetail(row, "Submitted / eligible offers", tender.Submitted + " / " + tender.Eligible);
                if (type != "awards") AddDetail(row, "Assigned committee", tender.Members + " members");
                if (tender.Report != null)
                {
                    AddDetail(row, "Results report", tender.Report.Status);
                    AddDetail(row, "Prepared by", tender.Report.PreparedBy);
                    AddDetail(row, "Report completed", DateText(tender.Report.SubmittedAt));
                }
            }
        }
        else if (type is "offers" or "my-offers")
        {
            var query = _db.Bids.AsNoTracking().Where(b => ids.Contains(b.Id));
            query = type == "my-offers" ? query.Where(b => b.SupplierProfile.UserId == userId)
                : query.Where(b => b.Status != Statuses.BidStatus.Draft && b.SubmittedAt != null);
            var bids = await query.Select(b => new { b.Id, b.DeliveryDays, b.ValidityDays, b.PaymentTerms, b.Currency,
                Documents = b.BidDocuments.Count(), ItemCount = b.BidItems.Count(),
                Items = b.BidItems.OrderBy(i => i.Id).Take(3).Select(i => new { i.RequisitionItem.ItemName, i.Quantity, i.UnitPrice }).ToList(),
                Compliance = b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                    .Select(c => new { c.Result, c.Reason }).FirstOrDefault() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var bid = bids.Single(b => b.Id.ToString() == row.RecordId);
                AddDetail(row, "Delivery / offer validity", bid.DeliveryDays + " days / " + bid.ValidityDays + " days");
                AddDetail(row, "Payment terms", bid.PaymentTerms);
                var items = string.Join("; ", bid.Items.Select(i => $"{i.ItemName}: {i.Quantity:0.##} × {i.UnitPrice:0.00} {bid.Currency}"));
                if (bid.ItemCount > bid.Items.Count) items += $"; plus {bid.ItemCount - bid.Items.Count} more item(s)";
                AddDetail(row, "Quoted items", items);
                AddDetail(row, "Attachments", bid.Documents + " file(s)");
                // Internal compliance notes are staff-only; suppliers retain their own offer data.
                if (type == "offers" && bid.Compliance != null)
                {
                    AddDetail(row, "Compliance result", bid.Compliance.Result);
                    AddDetail(row, "Compliance note", Excerpt(bid.Compliance.Reason));
                }
            }
        }
        else if (type == "my-invitations")
        {
            var invitations = await _db.Invitations.AsNoTracking().Where(i => ids.Contains(i.Id) && i.SupplierProfile.UserId == userId)
                .Select(i => new { i.Id, i.SentAt, i.ExpiresAt, i.RespondedAt, i.Tender.SubmissionDeadline }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var invitation = invitations.Single(i => i.Id.ToString() == row.RecordId);
                AddDetail(row, "Invitation sent", DateText(invitation.SentAt));
                AddDetail(row, "Invitation expires", DateText(invitation.ExpiresAt));
                AddDetail(row, "Your response", DateText(invitation.RespondedAt));
                AddDetail(row, "Offer deadline", DateText(invitation.SubmissionDeadline));
            }
        }
        else if (type == "review-reports")
        {
            var reports = await _db.EvaluationReports.AsNoTracking().Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, PreparedBy = r.PreparedByUser.FullName, r.SubmittedAt, r.Justification,
                    Recommended = r.RecommendedBid == null ? null : r.RecommendedBid.SupplierProfile.CompanyName }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var report = reports.Single(r => r.Id.ToString() == row.RecordId);
                AddDetail(row, "Prepared by", report.PreparedBy);
                AddDetail(row, "Committee report completed", DateText(report.SubmittedAt));
                AddDetail(row, "Recommended supplier", report.Recommended);
                AddDetail(row, "Report note", Excerpt(report.Justification));
            }
        }
        else if (type is "manager-decisions" or "committee-decisions")
        {
            var decisions = await _db.TenderApprovals.AsNoTracking().Where(a => ids.Contains(a.Id) && a.DecidedByUserId == userId)
                .Select(a => new { a.Id, a.Comment, a.Report.SubmittedAt,
                    Selected = a.SelectedBid == null ? null : a.SelectedBid.SupplierProfile.CompanyName }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var decision = decisions.Single(a => a.Id.ToString() == row.RecordId);
                AddDetail(row, "Report completed", DateText(decision.SubmittedAt));
                AddDetail(row, "Selected supplier", decision.Selected);
                AddDetail(row, "Decision note", Excerpt(decision.Comment));
            }
        }
        else if (type == "votes")
        {
            var votes = await _db.TopCommitteeVotes.AsNoTracking().Where(v => ids.Contains(v.Id) && v.MemberUserId == userId)
                .Select(v => new { v.Id, v.Comment, Selected = v.SelectedBid == null ? null : v.SelectedBid.SupplierProfile.CompanyName }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var vote = votes.Single(v => v.Id.ToString() == row.RecordId);
                AddDetail(row, "Vote comment", Excerpt(vote.Comment));
                AddDetail(row, "Selected supplier", vote.Selected);
            }
        }
    }

    private static void AddDetail(ReportRecordViewModel row, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) row.MoreDetails.Add(new ReportFieldViewModel { Label = label, Value = value.Trim() });
    }
    private static string? DateText(DateTime? value) => value.HasValue ? value.Value.ToString("dd MMM yyyy, HH:mm") + " UTC" : null;
    private static string? Excerpt(string? value) => value?.Length > 250 ? value[..250] + "… (excerpt)" : value;
}
