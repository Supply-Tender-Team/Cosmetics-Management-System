using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private async Task FillAdminBusinessDetailsAsync(string type, string userId, List<ReportRecordViewModel> rows, CancellationToken ct)
    {
        if (rows.Count == 0 || type == "admin-audit-trail") return;
        if (type == "admin-no-offers") type = "admin-tenders";
        if (type == "admin-excluded-offers") type = "admin-offers";
        if (type is "admin-manager-decisions" or "admin-top-decisions") type = "admin-decisions";
        var ids = rows.Select(r => int.Parse(r.RecordId)).ToList();
        foreach (var row in rows)
        { AddDetail(row, "Tender status", row.TenderStatus); AddDetail(row, "Department", row.Department); }

        if (type == "admin-awards")
        { await FillRecordDetailsAsync("awards", userId, rows, ct); return; }

        if (type == "admin-requests")
        {
            var requests = await _db.PurchaseRequisitions.AsNoTracking().Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, RequestedBy = r.RequestedByUser.FullName, r.Reason, r.Priority, r.TargetDate,
                    r.SubmittedAt, r.ReviewNote, ReviewedBy = r.ReviewedByUser == null ? null : r.ReviewedByUser.FullName,
                    Items = r.PurchaseRequisitionItems.Select(i => new { i.ItemName, i.Quantity, i.Unit, i.Specifications }).ToList(),
                    Tenders = r.Tenders.Select(t => t.TenderNumber + " — " + t.Status).ToList() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var request = requests.Single(r => r.Id.ToString() == row.RecordId);
                AddDetail(row, "Requested by", request.RequestedBy); AddDetail(row, "Priority", request.Priority);
                AddDetail(row, "Needed by", request.TargetDate.ToString("dd MMM yyyy"));
                AddDetail(row, "Submitted", DateText(request.SubmittedAt)); AddDetail(row, "Purpose", request.Reason);
                AddDetail(row, "Reviewed by", request.ReviewedBy); AddDetail(row, "Review note", request.ReviewNote);
                foreach (var item in request.Items) AddDetail(row, item.ItemName, $"{item.Quantity:0.##} {item.Unit}; {item.Specifications}");
                AddDetail(row, "Linked tenders", string.Join("; ", request.Tenders));
            }
        }
        else if (type == "admin-tenders")
        {
            await FillRecordDetailsAsync("tenders", userId, rows, ct);
            var tenders = await _db.Tenders.AsNoTracking().Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Description, t.Terms, t.PublishDate, t.RequiresTopCommittee,
                    CreatedBy = t.CreatedByUser.FullName, Members = t.CommitteeMembers.Select(m => m.User.FullName + " (" + m.MemberRole + ")").ToList(),
                    Suppliers = t.TenderSuppliers.Select(s => s.SupplierProfile.CompanyName).ToList() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var tender = tenders.Single(t => t.Id.ToString() == row.RecordId);
                AddDetail(row, "Created by", tender.CreatedBy); AddDetail(row, "Published", DateText(tender.PublishDate));
                AddDetail(row, "Top Committee required", tender.RequiresTopCommittee ? "Yes" : "No");
                AddDetail(row, "Assigned members", string.Join("; ", tender.Members));
                AddDetail(row, "Selected suppliers", string.Join("; ", tender.Suppliers));
                AddDetail(row, "Description", tender.Description); AddDetail(row, "Terms", tender.Terms);
            }
        }
        else if (type == "admin-supplier-activity")
        {
            await FillAdminDetailsAsync("suppliers", rows, ids, ct);
            var suppliers = await _db.SupplierProfiles.AsNoTracking().Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.TaxNumber, s.CommercialRegNumber, s.Country, s.City, s.StatusReason,
                    Drafts = s.Bids.Count(b => b.Status == Statuses.BidStatus.Draft),
                    Withdrawn = s.Bids.Count(b => b.Status == Statuses.BidStatus.Withdrawn),
                    Excluded = s.Bids.Count(b => b.Status == Statuses.BidStatus.NonCompliant),
                    Documents = s.SupplierDocuments.Count() }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var supplier = suppliers.Single(s => s.Id.ToString() == row.RecordId);
                AddDetail(row, "Invited tenders (distinct)", row.Count.ToString());
                AddDetail(row, "Tenders with current submitted offers (distinct)", row.SecondCount.ToString());
                AddDetail(row, "Awarded tenders", row.ReadyCount.ToString());
                AddDetail(row, "All-time draft / withdrawn / non-compliant offers", $"{supplier.Drafts} / {supplier.Withdrawn} / {supplier.Excluded}");
                AddDetail(row, "Tax / registration number", supplier.TaxNumber + " / " + supplier.CommercialRegNumber);
                AddDetail(row, "Location", supplier.Country + " / " + supplier.City);
                AddDetail(row, "Status reason", supplier.StatusReason); AddDetail(row, "Recorded legal documents", supplier.Documents.ToString());
            }
        }
        else if (type == "admin-documents")
        {
            foreach (var row in rows)
            {
                AddDetail(row, "Document type", row.Title); AddDetail(row, "Uploaded", DateText(row.OccurredAt));
                AddDetail(row, "Expiry date", row.SecondaryDate?.ToString("dd MMM yyyy") ?? "Not recorded");
            }
        }
        else if (type == "admin-offers")
        {
            var bids = await _db.Bids.AsNoTracking().Where(b => ids.Contains(b.Id))
                .Select(b => new { b.Id, b.Tender.Title, b.DeliveryDays, b.ValidityDays, b.PaymentTerms, b.TechnicalSummary,
                    Documents = b.BidDocuments.Count(), b.Currency,
                    Items = b.BidItems.Select(i => new { i.RequisitionItem.ItemName, i.Quantity, i.UnitPrice, i.TaxAmount, i.ShippingCost, i.InstallationCost, i.OfferedSpecifications }).ToList(),
                    Compliance = b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                        .Select(c => new { c.Result, c.Reason, c.CheckedAt, Reviewer = c.CheckedByUser.FullName }).FirstOrDefault(),
                    Recommended = b.Tender.EvaluationReports.Any(r => r.RecommendedBidId == b.Id),
                    Won = b.Tender.Status == Statuses.TenderStatus.Awarded && b.Tender.AwardedSupplierId == b.SupplierProfileId
                }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var bid = bids.Single(b => b.Id.ToString() == row.RecordId);
                AddDetail(row, "Tender title", bid.Title);
                AddDetail(row, "Delivery / validity", bid.DeliveryDays + " days / " + bid.ValidityDays + " days");
                AddDetail(row, "Payment terms", bid.PaymentTerms); AddDetail(row, "Technical summary", bid.TechnicalSummary);
                AddDetail(row, "Attachments", bid.Documents + " file(s)");
                foreach (var item in bid.Items)
                {
                    AddDetail(row, item.ItemName, $"{item.Quantity:0.##} × {item.UnitPrice:0.00} {bid.Currency}; tax {item.TaxAmount:0.00}; shipping {item.ShippingCost:0.00}; installation {item.InstallationCost ?? 0:0.00}. {item.OfferedSpecifications}");
                }
                if (bid.Compliance != null)
                {
                    AddDetail(row, "Compliance result", bid.Compliance.Result); AddDetail(row, "Compliance reason", bid.Compliance.Reason);
                    AddDetail(row, "Compliance checked by", bid.Compliance.Reviewer); AddDetail(row, "Compliance checked", DateText(bid.Compliance.CheckedAt));
                }
                AddDetail(row, "Committee recommendation", bid.Recommended ? "Recommended" : "No recorded recommendation for this offer");
                AddDetail(row, "Award", bid.Won ? "Awarded" : "No recorded award for this offer");
            }
        }
        else if (type == "admin-invitations")
        {
            var invitations = await _db.Invitations.AsNoTracking().Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.DeclineReason }).ToListAsync(ct);
            foreach (var row in rows)
            {
                AddDetail(row, "Sent", DateText(row.OccurredAt)); AddDetail(row, "Expires", DateText(row.SecondaryDate));
                AddDetail(row, "Responded", DateText(row.ThirdDate) ?? "No response recorded");
                AddDetail(row, "Decline reason", invitations.Single(i => i.Id.ToString() == row.RecordId).DeclineReason);
            }
        }
        else if (type == "admin-members")
        {
            var members = await _db.CommitteeMembers.AsNoTracking().Where(m => ids.Contains(m.Id))
                .Select(m => new { m.Id, m.User.FullName, m.User.Email, m.MemberRole, m.TenderId, m.UserId }).ToListAsync(ct);
            var tenderIds = members.Select(m => m.TenderId).Distinct().ToList();
            var submissions = await _db.EvaluationCommitteeSubmissions.AsNoTracking().Where(s => tenderIds.Contains(s.TenderId))
                .Select(s => new { s.TenderId, s.EvaluatorUserId, s.ReadyAt, s.VotedAt }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var member = members.Single(m => m.Id.ToString() == row.RecordId);
                var submission = submissions.SingleOrDefault(s => s.TenderId == member.TenderId && s.EvaluatorUserId == member.UserId);
                AddDetail(row, "Member", member.FullName + " — " + member.Email); AddDetail(row, "Committee seat", member.MemberRole);
                AddDetail(row, "Ready confirmed", DateText(submission?.ReadyAt) ?? "Not confirmed");
                AddDetail(row, "Recommendation choice submitted", DateText(submission?.VotedAt) ?? "Not submitted");
            }
        }
        else if (type == "admin-evaluations")
        {
            var evaluations = await _db.FinancialEvaluations.AsNoTracking().Where(e => ids.Contains(e.Id))
                .Select(e => new { e.Id, e.BidId, e.EvaluatorUserId, e.Notes, e.Score, e.ReadyAt, e.CompletedAt,
                    Criteria = e.Bid.Tender.EvaluationCriteria
                        .OrderBy(c => c.Id).Select(c => new { c.Id, c.Name, c.Type, c.MaxScore, c.WeightPercent }).ToList() }).ToListAsync(ct);
            var bidIds = evaluations.Select(e => e.BidId).Distinct().ToList();
            var scores = await _db.TechnicalEvaluations.AsNoTracking().Where(s => bidIds.Contains(s.BidId)).ToListAsync(ct);
            foreach (var row in rows)
            {
                var evaluation = evaluations.Single(e => e.Id.ToString() == row.RecordId);
                decimal points = 0; var complete = evaluation.Criteria.Count > 0;
                AddDetail(row, "Evaluated by", row.Person);
                foreach (var criterion in evaluation.Criteria)
                {
                    var score = scores.SingleOrDefault(s => s.BidId == evaluation.BidId && s.EvaluatorUserId == evaluation.EvaluatorUserId && s.CriterionId == criterion.Id);
                    if (score?.Score == null || criterion.MaxScore <= 0) complete = false;
                    else if (criterion.Type == Statuses.CriterionType.Technical) points += score.Score.Value / criterion.MaxScore * criterion.WeightPercent;
                    AddDetail(row, criterion.Name, (score?.Score?.ToString("0.##") ?? "Pending") + " / " + criterion.MaxScore + "; weight " + criterion.WeightPercent + "%" + (string.IsNullOrWhiteSpace(score?.Notes) ? "" : "; " + score.Notes));
                }
                AddDetail(row, "Technical points", complete ? points.ToString("0.##") + " / " + evaluation.Criteria.Where(c => c.Type == Statuses.CriterionType.Technical).Sum(c => c.WeightPercent) : "Pending");
                AddDetail(row, "Saved financial points", evaluation.Score?.ToString("0.##") ?? "Pending");
                AddDetail(row, "Financial note", evaluation.Notes); AddDetail(row, "Complete save", DateText(evaluation.CompletedAt));
                AddDetail(row, "Ready confirmed", DateText(evaluation.ReadyAt));
            }
        }
        else if (type == "admin-reports")
        { await FillRecordDetailsAsync("review-reports", userId, rows, ct); }
        else if (type == "admin-decisions")
        {
            var decisions = await _db.TenderApprovals.AsNoTracking().Where(a => ids.Contains(a.Id))
                .Select(a => new { a.Id, a.Comment, a.Level, DecidedBy = a.DecidedByUser.FullName,
                    Selected = a.SelectedBid == null ? null : a.SelectedBid.SupplierProfile.CompanyName }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var decision = decisions.Single(d => d.Id.ToString() == row.RecordId);
                AddDetail(row, "Decision stage", decision.Level); AddDetail(row, "Recorded by", decision.DecidedBy);
                AddDetail(row, "Selected supplier", decision.Selected); AddDetail(row, "Decision comment", decision.Comment);
                if (row.OccurredAt.HasValue && row.SecondaryDate.HasValue && row.OccurredAt >= row.SecondaryDate)
                    AddDetail(row, "Days since report completion", (row.OccurredAt.Value - row.SecondaryDate.Value).TotalDays.ToString("0.##"));
            }
        }
        else if (type == "admin-voting")
        {
            foreach (var row in rows)
            { AddDetail(row, "Member", row.Person); AddDetail(row, "Vote submitted", DateText(row.OccurredAt)); }
        }
    }
}
