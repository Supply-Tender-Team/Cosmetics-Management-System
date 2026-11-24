using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Evaluation.Services;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private async Task LoadRequirementInsightsAsync(ReportPageViewModel page, IQueryable<ReportRecordViewModel> query, CancellationToken ct)
    {
        var key = page.Selected!.Key;
        if (!key.StartsWith("admin-", StringComparison.Ordinal) && !key.StartsWith("committee-", StringComparison.Ordinal) && key != "suppliers") return;
        void Metric(string label, string value) => page.Metrics.Add(new() { Label = label, Value = value });
        Metric("Matching records", page.TotalCount.ToString(CultureInfo.InvariantCulture));
        page.CoverageNotes.Add("Filters use the labelled event date. Status distributions show current recorded states, not an inferred historical snapshot.");
        if (key is "suppliers" or "admin-supplier-activity" or "committee-supplier-statistics") {
            Metric("Approved suppliers in scope", (await query.CountAsync(r => r.Status == Statuses.SupplierStatus.Approved, ct)).ToString());
            Metric("Suspended suppliers in scope", (await query.CountAsync(r => r.Status == Statuses.SupplierStatus.Suspended, ct)).ToString());
        }
        if (page.Breakdown.Count > 0) {
            page.Charts.Add(new() { Title = page.Selected.StatusLabel + " distribution", FilterName = "status",
                Points = page.Breakdown.Select(x => new ReportChartPointViewModel { Label = x.Label, FilterValue = x.Label, Count = int.Parse(x.Value) }).ToList() });
        }
        if (page.Selected.HasDepartment) {
            var departments = await query.Where(r => r.DepartmentId != null).GroupBy(r => new { r.DepartmentId, r.Department })
                .Select(g => new { g.Key.DepartmentId, g.Key.Department, Count = g.Count() }).OrderByDescending(g => g.Count).ThenBy(g => g.Department).ToListAsync(ct);
            page.Charts.Add(new() { Title = "By department", FilterName = "departmentId",
                Points = departments.Select(d => new ReportChartPointViewModel { Label = d.Department, FilterValue = d.DepartmentId!.Value.ToString(), Count = d.Count }).ToList() });
        }
        if (key is "admin-tenders" or "admin-no-offers") {
            var ids = query.Select(r => r.TenderId);
            var pairs = await _db.Invitations.AsNoTracking().Where(i => ids.Contains(i.TenderId)).Select(i => new { i.TenderId, i.SupplierProfileId }).Distinct().ToListAsync(ct);
            var submitted = await _db.Bids.AsNoTracking().Where(b => ids.Contains(b.TenderId) && b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
                .Select(b => new { b.TenderId, b.SupplierProfileId }).Distinct().ToListAsync(ct);
            var responding = pairs.Count(i => submitted.Any(b => b.TenderId == i.TenderId && b.SupplierProfileId == i.SupplierProfileId));
            Metric("Invited supplier-tender pairs", pairs.Count.ToString());
            Metric("Invited pairs with current offers", responding.ToString());
            Metric("Invitation participation rate", pairs.Count == 0 ? "Not available" : Number(100m * responding / pairs.Count) + "%");
            page.CoverageNotes.Add("Participation = distinct invited supplier-tender pairs with a current submitted offer / distinct invited pairs. Drafts and withdrawn offers are excluded; uninvited offers are not in this denominator.");
            page.CoverageNotes.Add("Evaluation delay cannot be classified until an evaluation due date or approved SLA is defined. Offer submission deadlines are not evaluation deadlines.");
        }
        if (key == "admin-requests") page.CoverageNotes.Add("Approval timestamps are available in View details. Average approval time awaits the team's rule for first/last submission and returned/resubmitted request cycles.");
        if (key == "admin-supplier-activity") {
            Metric("Registered suppliers in scope", page.TotalCount.ToString());
            Metric("With submitted offers", (await query.CountAsync(r => r.SecondCount > 0, ct)).ToString());
            Metric("With awarded tenders", (await query.CountAsync(r => r.ReadyCount > 0, ct)).ToString());
            page.CoverageNotes.Add("Performance here means recorded participation, exclusions and awards, not delivery quality. Transaction and repeat-frequency definitions await team confirmation; awards are not signed contracts.");
        }
        if (key.StartsWith("committee-", StringComparison.Ordinal)) {
            page.CoverageNotes.Add("Supplier registration counts are global; activity and tender outcomes are restricted to completed tenders requiring Top Committee review. Date filters select supplier registration dates or tender creation dates as labelled. Activity counts are all-time within that committee scope. Transaction and repeat-frequency definitions are pending confirmation.");
            if (key == "committee-supplier-statistics") {
                Metric("Suppliers with committee awards", (await query.CountAsync(r => r.ReadyCount > 0, ct)).ToString());
                Metric("Suppliers with committee participation", (await query.CountAsync(r => r.SecondCount > 0, ct)).ToString());
            }
        }
        if (key == "suppliers") {
            var statuses = await query.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            foreach (var status in statuses) Metric(status.Key, status.Count.ToString());
        }
        if (key is "admin-documents" or "admin-guarantees") {
            Metric("Expired", (await query.CountAsync(r => r.Status == "Expired", ct)).ToString());
            Metric("Expiring within 30 days", (await query.CountAsync(r => r.Status == "Expires within 30 days", ct)).ToString());
            Metric("Expiry not recorded", (await query.CountAsync(r => r.Status == "Expiry not recorded", ct)).ToString());
            page.CoverageNotes.Add("Expiry is calculated against today's UTC date, independently of the upload-date filter. No stored per-document verification result/reviewer/time exists; supplier approval, uploads and verification codes are not proof of verification.");
        }
        if (key == "admin-award-notices") {
            var sent = await query.Where(r => r.Title == "AwardWinner" && r.Status == "Sent" && r.ThirdDate != null && r.SecondaryDate != null)
                .Select(r => new { r.TenderNumber, Created = r.SecondaryDate!.Value, Sent = r.ThirdDate!.Value }).ToListAsync(ct);
            var durations = sent.GroupBy(r => r.TenderNumber).Select(g => g.OrderBy(x => x.Sent).First()).Where(r => r.Sent >= r.Created).ToList();
            Metric("Winner emails sent (tenders)", durations.Count.ToString());
            Metric("Average creation to first outbox Sent", durations.Count == 0 ? "Not available" : Number((decimal)durations.Average(r => (r.Sent - r.Created).TotalHours)) + " hours");
            Metric("Email rows not sent", (await query.CountAsync(r => r.Status != "Sent", ct)).ToString());
            page.CoverageNotes.Add("The duration uses recorded Sent timestamps for winner emails queued within the selected dates. The outbox does not store delivery mode: in Development a saved email preview can also be marked Sent. This is recorded outbox completion, not proof of SMTP delivery or recipient receipt. Pending and failed rows are excluded; in-app notices are separate.");
        }
        if (key is "admin-offers" or "admin-excluded-offers" or "admin-unified-results") page.CoverageNotes.Add("Offer amounts are quoted alternatives, not actual expenditure. Price ranges compare current submitted offers only within the same tender and currency.");
        if (key == "admin-vote-results") page.CoverageNotes.Add("Vote counts are aggregated by tender. Member identities are not paired with their choices. A tied vote is not replaced with an inferred decision.");
        if (key == "admin-unified-results") page.CoverageNotes.Add("Weighted subtotals use the same EvaluationScoring function as Evaluation. Four assigned Ready members and complete valid criteria are required for final totals. Pending data remains pending; no price-based financial score is invented.");
        if (key == "admin-audit-trail") page.CoverageNotes.Add("One row per existing audit record. This is not a guarantee that every action was logged by every feature; raw records are not reconstructed. Individual vote choices and sensitive payload values are redacted.");
        if (key == "admin-awards") page.CoverageNotes.Add("Awarded offer values are not final contracts. No final-contract entity, document, signature date or final-contract amount is stored in the current schema.");
    }

    private async Task FillRequirementDetailsAsync(string type, List<ReportRecordViewModel> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => r.RecordId).ToList();
        if (type == "admin-audit-trail") {
            var logs = await _db.AuditLogs.AsNoTracking().Where(a => ids.Contains(a.Id.ToString())).ToListAsync(ct);
            foreach (var row in rows) {
                var log = logs.Single(a => a.Id.ToString() == row.RecordId);
                AddDetail(row, "Audit ID", log.Id.ToString()); AddDetail(row, "Actor", row.Person);
                AddDetail(row, "Action", log.Action); AddDetail(row, "Entity", log.EntityName + " #" + log.EntityId);
                AddDetail(row, "Recorded at", DateText(log.CreatedAt));
                // Individual vote payloads may identify the member's choice; retain the event but not that content.
                var privateVote = log.Action.Contains("Vote", StringComparison.OrdinalIgnoreCase);
                AddDetail(row, "Recorded details", privateVote ? "Vote recorded; individual choice is private." : SafeAuditText(log.NewValues));
                AddDetail(row, "Previous recorded value", privateVote ? "Private vote payload" : SafeAuditText(log.OldValues));
            }
        }
        if (type == "admin-guarantees") {
            var guarantees = await _db.BidGuarantees.AsNoTracking().Where(g => ids.Contains(g.Id.ToString()))
                .Select(g => new { g.Id, g.BidId, g.GuaranteeType, g.ReferenceNumber, g.IssuerName, g.BeneficiaryName, g.IssuedAt, g.ExpiresAt, Documents = g.BidDocuments.Count() }).ToListAsync(ct);
            foreach (var row in rows) {
                var g = guarantees.Single(g => g.Id.ToString() == row.RecordId);
                AddDetail(row, "Offer", "#" + g.BidId); AddDetail(row, "Type", g.GuaranteeType); AddDetail(row, "Issuer", g.IssuerName);
                AddDetail(row, "Reference", g.ReferenceNumber); AddDetail(row, "Beneficiary", g.BeneficiaryName);
                AddDetail(row, "Issued", DateText(g.IssuedAt)); AddDetail(row, "Expires", DateText(g.ExpiresAt));
                AddDetail(row, "Guarantee duration", g.IssuedAt.HasValue && g.ExpiresAt >= g.IssuedAt ? Number((decimal)(g.ExpiresAt!.Value - g.IssuedAt.Value).TotalDays) + " days" : "Not recorded / invalid date order");
                AddDetail(row, "Attached documents", g.Documents.ToString()); AddDetail(row, "Verification", "No stored verification result; upload and reference are not verification.");
            }
        }
        if (type == "admin-award-notices") {
            var emails = await _db.EmailOutbox.AsNoTracking().Where(e => ids.Contains(e.Id.ToString())).Select(e => new { e.Id, e.Category, e.Attempts, e.SentAt, e.Status, e.CreatedAt }).ToListAsync(ct);
            foreach (var row in rows) {
                var email = emails.Single(e => e.Id.ToString() == row.RecordId);
                AddDetail(row, "Channel", "Email outbox / " + email.Category); AddDetail(row, "Queued", DateText(email.CreatedAt));
                AddDetail(row, "Outbox marked Sent", email.Status == "Sent" ? DateText(email.SentAt) ?? "Not recorded" : "Not sent");
                AddDetail(row, "Attempts", email.Attempts.ToString()); AddDetail(row, "Tender created", DateText(row.SecondaryDate));
                if (email.Category == "AwardWinner" && email.Status == "Sent" && email.SentAt >= row.SecondaryDate && row.SecondaryDate != null)
                    AddDetail(row, "Tender creation to outbox Sent", Number((decimal)(email.SentAt!.Value - row.SecondaryDate.Value).TotalHours) + " hours");
                AddDetail(row, "Recipient receipt", "Not tracked; SMTP mode versus saved preview is not recorded in the outbox.");
            }
        }
        if (type is "admin-requests" or "admin-tenders" or "admin-no-offers") {
            var entity = type == "admin-requests" ? "PurchaseRequisition" : "Tender";
            var events = await _db.AuditLogs.AsNoTracking().Where(a => a.EntityName == entity && ids.Contains(a.EntityId))
                .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => new { a.EntityId, a.Action, a.CreatedAt }).ToListAsync(ct);
            foreach (var row in rows) {
                var recorded = events.Where(e => e.EntityId == row.RecordId).ToList();
                foreach (var e in recorded) AddDetail(row, "Recorded " + e.Action, DateText(e.CreatedAt));
                if (type == "admin-requests") AddDetail(row, "Approval-time definition", "Pending team rule for resubmission cycles; timestamps above are actual recorded events.");
                else {
                    var id = int.Parse(row.RecordId);
                    var link = "/Sourcing/CommitteeBrief/Index/" + id;
                    var released = await _db.Notifications.AsNoTracking().Where(n => n.Type == "TenderEvaluation" && n.Link == link).OrderBy(n => n.CreatedAt).Select(n => (DateTime?)n.CreatedAt).FirstOrDefaultAsync(ct);
                    var completed = await _db.EvaluationReports.AsNoTracking().Where(r => r.TenderId == id && r.SubmittedAt != null).OrderBy(r => r.SubmittedAt).Select(r => r.SubmittedAt).FirstOrDefaultAsync(ct);
                    AddDetail(row, "First recorded evaluation notification", DateText(released) ?? "Not recorded");
                    AddDetail(row, "First completed evaluation report", DateText(completed) ?? "Not completed / not recorded");
                    AddDetail(row, "Evaluation deadline / delay", "No evaluation deadline or approved SLA is recorded. Delay not classified.");
                    var invited = await _db.Invitations.AsNoTracking().Where(i => i.TenderId == id).Select(i => i.SupplierProfileId).Distinct().ToListAsync(ct);
                    var responding = await _db.Bids.AsNoTracking().Where(b => b.TenderId == id && b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn && invited.Contains(b.SupplierProfileId)).Select(b => b.SupplierProfileId).Distinct().CountAsync(ct);
                    AddDetail(row, "Invited-supplier participation", responding + " / " + invited.Count + (invited.Count == 0 ? " (no invitations)" : " = " + Number(100m * responding / invited.Count) + "%"));
                }
            }
        }
        if (type is "admin-offers" or "admin-excluded-offers") {
            var numbers = rows.Select(r => r.TenderNumber).Distinct().ToList();
            var ranges = await _db.Bids.AsNoTracking().Where(b => numbers.Contains(b.Tender.TenderNumber) && b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
                .GroupBy(b => new { b.Tender.TenderNumber, b.Currency }).Select(g => new { g.Key.TenderNumber, g.Key.Currency, Lowest = g.Min(b => b.TotalPrice), Highest = g.Max(b => b.TotalPrice), Count = g.Count() }).ToListAsync(ct);
            foreach (var row in rows) {
                var range = ranges.FirstOrDefault(g => g.TenderNumber == row.TenderNumber && g.Currency == row.Currency);
                if (range != null) AddDetail(row, "Current submitted price range (same tender and currency)", Number(range.Lowest) + " - " + Number(range.Highest) + " " + range.Currency + "; " + range.Count + " offers");
            }
        }
        if (type == "admin-awards") {
            var tenderIds = rows.Select(r => int.Parse(r.RecordId)).ToList();
            var decisions = await _db.TenderApprovals.AsNoTracking().Where(a => tenderIds.Contains(a.TenderId) && a.Decision == Statuses.ApprovalDecision.Approved
                && a.Level == (a.Tender.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager))
                .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).Select(a => new { a.TenderId, a.SelectedBidId, a.DecidedAt, Matches = a.SelectedBid != null && a.SelectedBid.SupplierProfileId == a.Tender.AwardedSupplierId }).ToListAsync(ct);
            foreach (var row in rows) {
                var decision = decisions.FirstOrDefault(a => a.TenderId.ToString() == row.RecordId && a.Matches);
                AddDetail(row, "Award decision date", DateText(row.OccurredAt) ?? "Not recorded; creation date is not substituted");
                AddDetail(row, "Recorded amount source", decision?.Matches == true ? "Offer #" + decision.SelectedBidId + " selected by the final approving stage" : "Legacy fallback: latest current submitted offer of the recorded awarded supplier; not a final contract");
                AddDetail(row, "Final contract", "No final-contract record or signed contract is stored in the current model.");
            }
        }
        if (type == "admin-documents") foreach (var row in rows) AddDetail(row, "Legal verification", "Per-document result, reviewer and verification timestamp are not stored. Supplier approval is a separate status.");
        if (type == "admin-vote-results") {
            foreach (var row in rows) {
                var id = int.Parse(row.RecordId);
                var counts = await _db.TopCommitteeVotes.AsNoTracking().Where(v => v.TenderId == id).GroupBy(v => v.Vote).Select(g => new { Vote = g.Key, Count = g.Count() }).ToListAsync(ct);
                foreach (var v in counts) AddDetail(row, "Votes: " + v.Vote, v.Count.ToString());
                var final = await _db.TenderApprovals.AsNoTracking().Where(a => a.TenderId == id && a.Level == Statuses.ApprovalLevel.TopCommittee).OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).Select(a => new { a.Decision, a.DecidedAt, a.Comment }).FirstOrDefaultAsync(ct);
                AddDetail(row, "Final recorded decision", final?.Decision ?? "Not recorded");
                if (final != null) { AddDetail(row, "Decided", DateText(final.DecidedAt)); AddDetail(row, "Decision comment", final.Comment); }
                row.Detail = string.Join(" / ", counts.Select(v => v.Vote + ": " + v.Count));
            }
        }
        if (type == "committee-supplier-statistics") foreach (var row in rows) {
            AddDetail(row, "Invitations to completed committee tenders", row.Count.ToString());
            AddDetail(row, "Participated committee tenders (distinct)", row.SecondCount.ToString());
            AddDetail(row, "Awarded committee tenders", row.ReadyCount.ToString());
            AddDetail(row, "Transactions / repeat frequency", "Definition pending; awards are not labelled as contracts or transactions.");
            row.Detail = row.SecondCount + " participated / " + row.ReadyCount + " awarded committee tenders";
        }
        if (type == "committee-tender-statistics") foreach (var row in rows) {
            AddDetail(row, "Distinct invited suppliers", row.Count.ToString()); AddDetail(row, "Current submitted offers", row.SecondCount.ToString());
            row.Detail = row.Count + " invited / " + row.SecondCount + " submitted offers";
        }
        if (type == "admin-unified-results") await FillUnifiedResultsAsync(rows, ct);
    }

    private static string SafeAuditText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Not recorded";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?i)password|api[_ -]?key|secret|bearer|access[_ -]?token|nvapi-")) return "Sensitive audit payload redacted";
        return text;
    }

    private async Task FillUnifiedResultsAsync(List<ReportRecordViewModel> rows, CancellationToken ct)
    {
        var numbers = rows.Select(r => r.TenderNumber).Distinct().ToList();
        var tenders = await _db.Tenders.AsNoTracking().Where(t => numbers.Contains(t.TenderNumber)).Include(t => t.CommitteeMembers)
            .Include(t => t.EvaluationCriteria).Include(t => t.Bids).ThenInclude(b => b.TechnicalEvaluations)
            .Include(t => t.Bids).ThenInclude(b => b.FinancialEvaluations).Include(t => t.EvaluationReports).AsSplitQuery().ToListAsync(ct);
        foreach (var row in rows) {
            var tender = tenders.Single(t => t.TenderNumber == row.TenderNumber); var bid = tender.Bids.Single(b => b.Id.ToString() == row.RecordId);
            var members = tender.CommitteeMembers.ToList();
            var criteria = tender.EvaluationCriteria; var technical = criteria.Where(c => c.Type == Statuses.CriterionType.Technical).ToList();
            var financial = criteria.Where(c => c.Type == Statuses.CriterionType.Financial).ToList();
            var legacy = tender.EvaluationReports.Any(r => r.SubmittedAt != null) && financial.Count == 1 && financial[0].Name == "Eligible price ranking" && financial[0].MaxScore == 1
                && !tender.Bids.Any(b => b.TechnicalEvaluations.Any(e => e.CriterionId == financial[0].Id));
            var seats = new[] { Statuses.CommitteeRole.Department, Statuses.CommitteeRole.Financial, Statuses.CommitteeRole.Technical, Statuses.CommitteeRole.SourcingManager };
            var valid = EvaluationScoring.ValidRubric(criteria) && members.Count == 4 && members.Select(m => m.UserId).Distinct().Count() == 4
                && seats.All(seat => members.Count(m => m.MemberRole == seat) == 1);
            var ready = members.Count(m => bid.FinancialEvaluations.Any(e => e.EvaluatorUserId == m.UserId && e.ReadyAt != null));
            var tech = members.Select(m => EvaluationScoring.Points(technical, bid.TechnicalEvaluations, m.UserId)).ToList();
            var fin = members.Select(m => legacy ? bid.FinancialEvaluations.FirstOrDefault(e => e.EvaluatorUserId == m.UserId && e.ReadyAt != null)?.Score
                : EvaluationScoring.Points(financial, bid.TechnicalEvaluations, m.UserId)).ToList();
            var complete = valid && ready == 4 && tech.All(x => x.HasValue) && fin.All(x => x.HasValue && x >= 0 && x <= financial.Sum(c => c.WeightPercent));
            AddDetail(row, "Technical weight", Number(technical.Sum(c => c.WeightPercent)) + "%");
            AddDetail(row, "Financial weight", Number(financial.Sum(c => c.WeightPercent)) + "%");
            AddDetail(row, "Ready members", ready + " / 4");
            AddDetail(row, "Technical subtotal", complete ? Number(tech.Average()!.Value) : "Pending / incomplete configuration");
            AddDetail(row, "Financial subtotal", complete ? Number(fin.Average()!.Value) : "Pending / incomplete configuration");
            AddDetail(row, "Total out of 100", complete ? Number(tech.Average()!.Value + fin.Average()!.Value) : "Pending");
            AddDetail(row, "Scoring method", legacy ? "Historical saved price-rule financial subtotal; not recalculated" : "Average of four Ready members' dynamic weighted question scores");
            var report = tender.EvaluationReports.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefault();
            AddDetail(row, "Report status", report?.Status ?? "No report"); AddDetail(row, "Report submitted", DateText(report?.SubmittedAt));
            AddDetail(row, "Committee recommendation", report?.RecommendedBidId == bid.Id ? "Recommended in saved committee report" : "Not the saved recommendation");
            row.Detail = complete ? "Technical " + Number(tech.Average()!.Value) + " / Financial " + Number(fin.Average()!.Value) + " / Total " + Number(tech.Average()!.Value + fin.Average()!.Value) : "Pending complete committee evaluation";
        }
    }
}
