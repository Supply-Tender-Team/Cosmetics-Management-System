using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private IQueryable<ReportRecordViewModel> CreateGovernanceQuery(string type)
    {
        if (type == "admin-evaluation-delays") {
            var now = DateTime.UtcNow;
            return from t in _db.Tenders.AsNoTracking()
                   where t.PublishDate != null && t.Status != Statuses.TenderStatus.Draft
                   let schedule = _db.EvaluationSchedules.Where(s => s.TenderId == t.Id).FirstOrDefault()
                   let completed = t.EvaluationReports.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Select(r => r.SubmittedAt).FirstOrDefault()
                   select new ReportRecordViewModel {
                       RecordId = t.Id.ToString(), TenderId = t.Id, Reference = t.TenderNumber, Title = t.Title, TenderNumber = t.TenderNumber,
                       TenderStatus = t.Status, DepartmentId = t.Requisition.DepartmentId, Department = t.Requisition.Department.Name,
                       OccurredAt = t.CreatedAt, SecondaryDate = schedule == null ? null : schedule.EvaluationStartedAt,
                       ThirdDate = schedule == null ? null : schedule.EvaluationDueAt, EvaluationCompletedAt = completed,
                       Detail = "Saved evaluation schedule",
                       Status = schedule == null || schedule.EvaluationDueAt < schedule.EvaluationStartedAt || schedule.EvaluationStartedAt > now
                           || (completed != null && (completed < schedule.EvaluationStartedAt || completed > now)) ? "Unknown"
                           : completed != null ? completed > schedule.EvaluationDueAt ? "Completed late" : "Completed on time"
                           : now > schedule.EvaluationDueAt ? "Overdue" : "In progress"
                   };
        }
        // Both targets use the same projection so each uploaded target has one row, not one row per historical decision.
        var documents = _db.SupplierDocuments.AsNoTracking().Select(d => new ReportRecordViewModel {
            RecordId = "D:" + d.Id, Reference = "Legal document #" + d.Id, Title = d.SupplierProfile.CompanyName, Detail = d.DocType,
            VerificationDocumentId = d.Id, VerificationGuaranteeId = (int?)null,
            Status = _db.DocumentVerifications.Where(v => v.SupplierDocumentId == d.Id).OrderByDescending(v => v.Id).Select(v => v.Status).FirstOrDefault() ?? "No verification recorded",
            OccurredAt = d.UploadedAt, SupplierId = d.SupplierProfileId, Supplier = d.SupplierProfile.CompanyName,
            TenderId = (int?)null, TenderNumber = "", TenderStatus = "", DepartmentId = (int?)null, Department = "",
            PersonId = _db.DocumentVerifications.Where(v => v.SupplierDocumentId == d.Id).OrderByDescending(v => v.Id).Select(v => v.VerifiedByUserId).FirstOrDefault(),
            Person = _db.DocumentVerifications.Where(v => v.SupplierDocumentId == d.Id).OrderByDescending(v => v.Id).Select(v => v.VerifiedByUser.FullName).FirstOrDefault() ?? ""
        });
        var guarantees = _db.BidGuarantees.AsNoTracking().Select(g => new ReportRecordViewModel {
            RecordId = "G:" + g.Id, Reference = "Guarantee #" + g.Id, Title = g.Bid.SupplierProfile.CompanyName, Detail = g.GuaranteeType,
            VerificationDocumentId = (int?)null, VerificationGuaranteeId = g.Id,
            Status = _db.DocumentVerifications.Where(v => v.BidGuaranteeId == g.Id).OrderByDescending(v => v.Id).Select(v => v.Status).FirstOrDefault() ?? "No verification recorded",
            OccurredAt = g.CreatedAt, SupplierId = g.Bid.SupplierProfileId, Supplier = g.Bid.SupplierProfile.CompanyName,
            TenderId = g.Bid.TenderId, TenderNumber = g.Bid.Tender.TenderNumber, TenderStatus = g.Bid.Tender.Status,
            DepartmentId = g.Bid.Tender.Requisition.DepartmentId, Department = g.Bid.Tender.Requisition.Department.Name,
            PersonId = _db.DocumentVerifications.Where(v => v.BidGuaranteeId == g.Id).OrderByDescending(v => v.Id).Select(v => v.VerifiedByUserId).FirstOrDefault(),
            Person = _db.DocumentVerifications.Where(v => v.BidGuaranteeId == g.Id).OrderByDescending(v => v.Id).Select(v => v.VerifiedByUser.FullName).FirstOrDefault() ?? ""
        });
        return documents.Concat(guarantees);
    }

    private async Task AddGovernanceInsightsAsync(ReportPageViewModel page, IQueryable<ReportRecordViewModel> query, CancellationToken ct)
    {
        var key = page.Selected!.Key;
        void Metric(string label, string value) => page.Metrics.Add(new() { Label = label, Value = value });
        if (key == "admin-requests") {
            var source = await query.Select(r => new { r.RecordId, r.Status }).ToListAsync(ct);
            var ids = source.Select(r => r.RecordId).ToList();
            var events = await _db.AuditLogs.AsNoTracking().Where(a => a.EntityName == "PurchaseRequisition" && ids.Contains(a.EntityId)
                && (a.Action == "Submitted" || a.Action == "Approved")).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToListAsync(ct);
            var hours = new List<decimal>();
            foreach (var row in source.Where(r => r.Status == Statuses.RequestStatus.Approved || r.Status == Statuses.RequestStatus.ConvertedToTender)) {
                var timing = ReportTimingRules.ApprovalTiming(events.Where(a => a.EntityId == row.RecordId), DateTime.UtcNow);
                if (timing.Hours.HasValue) hours.Add(timing.Hours.Value);
            }
            Metric("Average approval time", hours.Count == 0 ? "N/A" : Number(hours.Average()) + " calendar hours");
            Metric("Requests with approval timing", hours.Count.ToString());
            Metric("Approved requests missing valid timing", (source.Count(r => r.Status == Statuses.RequestStatus.Approved || r.Status == Statuses.RequestStatus.ConvertedToTender) - hours.Count).ToString());
            Metric("Returned requests", source.Count(r => r.Status == Statuses.RequestStatus.Returned).ToString());
            Metric("Rejected requests", source.Count(r => r.Status == Statuses.RequestStatus.Rejected).ToString());
            page.CoverageNotes.Add("Approval time = latest Submitted audit event preceding the latest Approved event, in calendar hours. Only currently Approved/Converted requests with both valid events enter the average; Returned/Rejected are separate. Filters select request creation dates.");
        }
        if (key is "admin-transactions" or "committee-transactions") {
            var awards = await query.Where(r => r.OccurredAt != null && r.SupplierId != null).Select(r => new { r.SupplierId, r.RecordId }).Distinct().ToListAsync(ct);
            var groups = awards.GroupBy(a => a.SupplierId).ToList(); var repeat = groups.Count(g => g.Select(a => a.RecordId).Distinct().Count() > 1);
            Metric("Awarded-tender transactions", awards.Count.ToString()); Metric("Suppliers with transactions", groups.Count.ToString());
            Metric("Repeat suppliers", repeat.ToString()); var rate = ReportTimingRules.RepeatRate(repeat, groups.Count);
            Metric("Supplier repeat rate", rate.HasValue ? Number(rate.Value) + "%" : "N/A");
            Metric("Awards missing a final approval date", (await query.CountAsync(r => r.OccurredAt == null, ct)).ToString());
            page.CoverageNotes.Add("Transaction = distinct awarded tender dated by final approval, not a signed contract. Repeat rate = suppliers with more than one transaction / suppliers with at least one transaction within these filters. No dates means all time. Missing approval dates are excluded; zero awarded suppliers gives N/A.");
        }
        if (key == "admin-evaluation-delays") {
            foreach (var status in new[] { "Overdue", "Completed late", "Completed on time", "In progress", "Unknown" }) Metric(status, (await query.CountAsync(r => r.Status == status, ct)).ToString());
            page.CoverageNotes.Add("Uses the saved first-share start and due date. Rework keeps that deadline. Completion is the latest committee report's SubmittedAt. Missing/invalid dates are Unknown; no default deadline is invented. Date filters select tender creation.");
        }
        if (key == "admin-contracts") {
            Metric("Final approved awards", page.TotalCount.ToString());
            page.CoverageNotes.Add("Project definition: Final award decision report fulfils final-contract reporting. Only Awarded tenders with a recorded final Approved decision and matching selected supplier offer are included. Supplier acceptance uses the actual invitation response; missing dates remain Not recorded. This is not a separate signing record. Prices are recorded selected-offer values; currencies remain separate.");
        }
        if (key == "admin-verifications") {
            foreach (var status in new[] { Statuses.VerificationStatus.Verified, Statuses.VerificationStatus.Rejected, Statuses.VerificationStatus.Pending, "No verification recorded" })
                Metric(status, (await query.CountAsync(r => r.Status == status, ct)).ToString());
            page.CoverageNotes.Add("Newest verification ID is current; previous decisions remain history. No row is not Verified. Date filters select document upload or guarantee creation, not the review date. Expiry and verification are independent.");
        }
        if (key is "admin-documents" or "admin-guarantees") {
            var ids = query.Select(r => r.RecordId);
            var current = new TenderSystem.Services.Governance.VerificationService(_db).Current();
            var matches = key == "admin-documents" ? current.Where(v => v.SupplierDocumentId != null && ids.Contains(v.SupplierDocumentId.Value.ToString()))
                : current.Where(v => v.BidGuaranteeId != null && ids.Contains(v.BidGuaranteeId.Value.ToString()));
            foreach (var status in new[] { Statuses.VerificationStatus.Verified, Statuses.VerificationStatus.Rejected, Statuses.VerificationStatus.Pending })
                Metric("Verification: " + status, (await matches.CountAsync(v => v.Status == status, ct)).ToString());
            Metric("No verification recorded", (page.TotalCount - await matches.CountAsync(ct)).ToString());
        }
    }

    private async Task FillGovernanceDetailsAsync(string type, List<ReportRecordViewModel> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        if (type is "admin-documents" or "admin-guarantees" or "admin-verifications") {
            var docIds = type == "admin-documents" ? rows.Select(r => int.Parse(r.RecordId)).ToList() : rows.Where(r => r.VerificationDocumentId != null).Select(r => r.VerificationDocumentId!.Value).ToList();
            var guaranteeIds = type == "admin-guarantees" ? rows.Select(r => int.Parse(r.RecordId)).ToList() : rows.Where(r => r.VerificationGuaranteeId != null).Select(r => r.VerificationGuaranteeId!.Value).ToList();
            var history = await _db.DocumentVerifications.AsNoTracking().Where(v => (v.SupplierDocumentId != null && docIds.Contains(v.SupplierDocumentId.Value)) || (v.BidGuaranteeId != null && guaranteeIds.Contains(v.BidGuaranteeId.Value)))
                .OrderByDescending(v => v.Id).Select(v => new { v.Id, v.SupplierDocumentId, v.BidGuaranteeId, v.Status, v.Method, v.Reason, v.VerifiedAt, Reviewer = v.VerifiedByUser.FullName }).ToListAsync(ct);
            foreach (var row in rows) {
                var docId = type == "admin-documents" ? int.Parse(row.RecordId) : row.VerificationDocumentId;
                var guaranteeId = type == "admin-guarantees" ? int.Parse(row.RecordId) : row.VerificationGuaranteeId;
                var records = history.Where(v => (docId != null && v.SupplierDocumentId == docId) || (guaranteeId != null && v.BidGuaranteeId == guaranteeId)).ToList();
                AddDetail(row, "Current verification", records.FirstOrDefault()?.Status ?? "No verification recorded");
                foreach (var v in records) AddDetail(row, "Verification #" + v.Id + (v == records.First() ? " (current)" : " (history)"),
                    v.Status + "; " + v.Reviewer + "; " + DateText(v.VerifiedAt) + "; " + v.Method + "; " + v.Reason);
            }
        }
        if (type == "admin-contracts") {
            var ids = rows.Select(r => int.Parse(r.RecordId)).ToList();
            var decisions = await _db.TenderApprovals.AsNoTracking().Where(a => ids.Contains(a.TenderId)
                && a.Decision == Statuses.ApprovalDecision.Approved
                && a.Level == (a.Tender.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager)
                && a.SelectedBid != null && a.SelectedBid.SupplierProfileId == a.Tender.AwardedSupplierId)
                .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id)
                .Select(a => new { a.TenderId, a.SelectedBidId, a.DecidedAt, a.Level, a.Comment, By = a.DecidedByUser.FullName,
                    a.Tender.Description, a.Tender.Terms, a.SelectedBid!.PaymentTerms, a.SelectedBid.DeliveryDays, a.SelectedBid.ValidityDays }).ToListAsync(ct);
            var acceptance = await _db.Invitations.AsNoTracking().Where(i => ids.Contains(i.TenderId)
                && i.Status == Statuses.InvitationStatus.Accepted).OrderByDescending(i => i.RespondedAt).ThenByDescending(i => i.Id)
                .Select(i => new { i.TenderId, i.SupplierProfileId, i.RespondedAt }).ToListAsync(ct);
            foreach (var row in rows) {
                var decision = decisions.First(a => a.TenderId.ToString() == row.RecordId);
                var accepted = acceptance.FirstOrDefault(i => i.TenderId == decision.TenderId && i.SupplierProfileId == row.SupplierId);
                AddDetail(row, "Final approval", decision.Level + "; " + decision.By + "; " + DateText(decision.DecidedAt));
                AddDetail(row, "Selected offer", "Offer #" + decision.SelectedBidId);
                AddDetail(row, "Decision notes", decision.Comment);
                AddDetail(row, "Tender scope", decision.Description); AddDetail(row, "Tender terms", decision.Terms);
                AddDetail(row, "Payment terms", decision.PaymentTerms); AddDetail(row, "Delivery days", decision.DeliveryDays.ToString());
                AddDetail(row, "Offer validity days", decision.ValidityDays.ToString());
                AddDetail(row, "Supplier acceptance", accepted == null ? "Not recorded" : "Accepted tender invitation");
                AddDetail(row, "Supplier acceptance date", DateText(accepted?.RespondedAt) ?? "Not recorded");
            }
        }
        if (type is "admin-evaluation-delays" or "admin-tenders" or "admin-no-offers") {
            var ids = rows.Select(r => int.Parse(r.RecordId)).ToList();
            var schedules = await _db.EvaluationSchedules.AsNoTracking().Where(s => ids.Contains(s.TenderId)).ToListAsync(ct);
            var reports = await _db.EvaluationReports.AsNoTracking().Where(r => ids.Contains(r.TenderId)).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Select(r => new { r.TenderId, r.SubmittedAt }).ToListAsync(ct);
            foreach (var row in rows) {
                var schedule = schedules.SingleOrDefault(s => s.TenderId.ToString() == row.RecordId);
                var completion = reports.FirstOrDefault(r => r.TenderId.ToString() == row.RecordId)?.SubmittedAt;
                var state = ReportTimingRules.EvaluationStatus(schedule?.EvaluationStartedAt, schedule?.EvaluationDueAt, completion, DateTime.UtcNow);
                AddDetail(row, "Saved evaluation start", DateText(schedule?.EvaluationStartedAt) ?? "Unknown / no schedule");
                AddDetail(row, "Saved evaluation deadline", DateText(schedule?.EvaluationDueAt) ?? "Unknown / no schedule");
                AddDetail(row, "Saved SLA days", schedule?.SlaDays.ToString() ?? "Unknown"); AddDetail(row, "Evaluation completed", DateText(completion) ?? "Not submitted");
                AddDetail(row, "Evaluation timing", state);
                var hours = ReportTimingRules.DelayHours(schedule?.EvaluationStartedAt, schedule?.EvaluationDueAt, completion, DateTime.UtcNow);
                AddDetail(row, "Hours beyond due date", hours.HasValue ? Number(hours.Value) : "Unknown");
            }
        }
        if (type is "admin-transactions" or "committee-transactions") foreach (var row in rows) {
            AddDetail(row, "Transaction definition", "One distinct awarded tender, dated by final approval");
            AddDetail(row, "Final approval date", DateText(row.OccurredAt) ?? "Unknown; excluded from period and repeat metrics");
        }
        if (type == "admin-requests") {
            var ids = rows.Select(r => r.RecordId).ToList();
            var events = await _db.AuditLogs.AsNoTracking().Where(a => a.EntityName == "PurchaseRequisition" && ids.Contains(a.EntityId) && (a.Action == "Submitted" || a.Action == "Approved"))
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToListAsync(ct);
            foreach (var row in rows) {
                var timing = ReportTimingRules.ApprovalTiming(events.Where(a => a.EntityId == row.RecordId), DateTime.UtcNow);
                AddDetail(row, "Approval timing submission", DateText(timing.SubmittedAt) ?? "Not recorded");
                AddDetail(row, "Approval timing decision", DateText(timing.ApprovedAt) ?? "Not recorded");
                var eligible = row.Status == Statuses.RequestStatus.Approved || row.Status == Statuses.RequestStatus.ConvertedToTender;
                AddDetail(row, "Approval calendar hours", eligible && timing.Hours.HasValue ? Number(timing.Hours.Value) : "Not in average / timing missing or inconsistent");
            }
        }
    }
}
