using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    private IQueryable<ReportRecordViewModel> CreateAdminQuery(string type, ReportFilterViewModel? filter = null)
    {
        var today = DateTime.UtcNow.Date;
        var from = filter?.From?.Date;
        var until = filter?.To?.Date.AddDays(1);
        switch (type)
        {
            case "admin-contracts":
                return CreateAdminQuery("admin-awards", filter).Where(r => _db.TenderApprovals.Any(a => a.TenderId == r.TenderId
                    && a.Decision == Statuses.ApprovalDecision.Approved
                    && a.Level == (a.Tender.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager)
                    && a.SelectedBid != null && a.SelectedBid.SupplierProfileId == r.SupplierId));
            case "admin-verifications":
            case "admin-evaluation-delays":
                return CreateGovernanceQuery(type);
            case "admin-transactions":
                return CreateAdminQuery("admin-awards", filter);
            case "admin-unified-results":
                return CreateAdminQuery("admin-offers", filter).Where(r => r.OccurredAt != null && (r.Status == Statuses.BidStatus.Compliant || r.Status == Statuses.BidStatus.Accepted));
            case "admin-vote-results":
                return CreateAdminQuery("admin-tenders", filter).Where(r => _db.TopCommitteeVotes.Any(v => v.TenderId == r.TenderId)
                    || _db.TenderApprovals.Any(a => a.TenderId == r.TenderId && a.Level == Statuses.ApprovalLevel.TopCommittee));
            case "admin-audit-trail":
                return _db.AuditLogs.AsNoTracking().Select(a => new ReportRecordViewModel {
                    RecordId = a.Id.ToString(), Reference = a.Action, Title = a.EntityName + " #" + a.EntityId,
                    Detail = a.User == null ? "System / unidentified actor" : a.User.FullName,
                    PersonId = a.UserId, Person = a.User == null ? "System / unidentified actor" : a.User.FullName,
                    Status = a.Action, OccurredAt = a.CreatedAt
                });
            case "admin-guarantees":
                return _db.BidGuarantees.AsNoTracking().Select(g => new ReportRecordViewModel {
                    RecordId = g.Id.ToString(), Reference = g.ReferenceNumber ?? ("Guarantee #" + g.Id), Title = g.Bid.SupplierProfile.CompanyName,
                    Detail = g.GuaranteeType, TenderNumber = g.Bid.Tender.TenderNumber, TenderId = g.Bid.TenderId, TenderStatus = g.Bid.Tender.Status,
                    SupplierId = g.Bid.SupplierProfileId, Supplier = g.Bid.SupplierProfile.CompanyName,
                    DepartmentId = g.Bid.Tender.Requisition.DepartmentId, Department = g.Bid.Tender.Requisition.Department.Name,
                    OccurredAt = g.CreatedAt, SecondaryDate = g.ExpiresAt, ThirdDate = g.IssuedAt, Amount = g.Amount, Currency = g.Currency,
                    Status = g.ExpiresAt == null ? "Expiry not recorded" : g.ExpiresAt < today ? "Expired" : g.ExpiresAt <= today.AddDays(30) ? "Expires within 30 days" : "Not expired"
                });
            case "admin-award-notices":
                return _db.EmailOutbox.AsNoTracking().Where(e => e.RelatedEntity == "Tender" && (e.Category == "AwardWinner" || e.Category == "AwardUnsuccessful"))
                    .Select(e => new ReportRecordViewModel {
                        RecordId = e.Id.ToString(), Reference = "Email #" + e.Id, Title = e.Category, Status = e.Status,
                        DeliveryMode = e.DeliveryMode == null || e.DeliveryMode.Trim() == "" ? "Unknown" : e.DeliveryMode, Detail = e.Category + " / " + (e.DeliveryMode == null || e.DeliveryMode.Trim() == "" ? "Unknown" : e.DeliveryMode), OccurredAt = e.CreatedAt, ThirdDate = e.SentAt,
                        TenderNumber = _db.Tenders.Where(t => t.Id.ToString() == e.RelatedId).Select(t => t.TenderNumber).FirstOrDefault() ?? "",
                        TenderStatus = _db.Tenders.Where(t => t.Id.ToString() == e.RelatedId).Select(t => t.Status).FirstOrDefault() ?? "",
                        DepartmentId = _db.Tenders.Where(t => t.Id.ToString() == e.RelatedId).Select(t => (int?)t.Requisition.DepartmentId).FirstOrDefault(),
                        Department = _db.Tenders.Where(t => t.Id.ToString() == e.RelatedId).Select(t => t.Requisition.Department.Name).FirstOrDefault() ?? "",
                        SupplierId = _db.SupplierProfiles.Where(s => e.DedupKey == (e.Category == "AwardWinner" ? "award-winner:" : "award-unsuccessful:") + e.RelatedId + ":" + s.Id.ToString()).Select(s => (int?)s.Id).FirstOrDefault(),
                        Supplier = _db.SupplierProfiles.Where(s => e.DedupKey == (e.Category == "AwardWinner" ? "award-winner:" : "award-unsuccessful:") + e.RelatedId + ":" + s.Id.ToString()).Select(s => s.CompanyName).FirstOrDefault() ?? "",
                        SecondaryDate = _db.Tenders.Where(t => t.Id.ToString() == e.RelatedId).Select(t => (DateTime?)t.CreatedAt).FirstOrDefault()
                    });
            case "admin-no-offers":
                return CreateAdminQuery("admin-tenders", filter).Where(r => r.SecondCount == 0 && r.Status != Statuses.TenderStatus.Draft
                    && _db.Tenders.Any(t => t.Id == r.TenderId && t.PublishDate != null));
            case "admin-excluded-offers":
                return CreateAdminQuery("admin-offers", filter).Where(r => r.Status == Statuses.BidStatus.NonCompliant || r.Status == Statuses.BidStatus.Rejected || r.Compliance == Statuses.ComplianceResult.NonCompliant);
            case "admin-manager-decisions":
                return CreateAdminQuery("admin-decisions", filter).Where(r => r.DecisionLevel == Statuses.ApprovalLevel.Manager);
            case "admin-top-decisions":
                return CreateAdminQuery("admin-decisions", filter).Where(r => r.DecisionLevel == Statuses.ApprovalLevel.TopCommittee);
            case "admin-requests":
                return _db.PurchaseRequisitions.AsNoTracking().Select(r => new ReportRecordViewModel
                {
                    RecordId = r.Id.ToString(), RequestId = r.Id, Reference = r.RequestNumber, Title = r.Title,
                    Status = r.Status, OccurredAt = r.CreatedAt, Amount = r.EstimatedBudget, Currency = "Unspecified",
                    DepartmentId = r.DepartmentId, Department = r.Department.Name,
                    PersonId = r.RequestedByUserId, Person = r.RequestedByUser.FullName,
                    Detail = r.RequestedByUser.FullName,
                    TenderNumber = r.Tenders.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                        .Select(t => t.TenderNumber).FirstOrDefault() ?? ""
                });
            case "admin-tenders":
                return _db.Tenders.AsNoTracking().Select(t => new ReportRecordViewModel
                {
                    RecordId = t.Id.ToString(), TenderId = t.Id, Reference = t.TenderNumber, TenderNumber = t.TenderNumber,
                    Title = t.Title, Detail = "", Status = t.Status, TenderStatus = t.Status, OccurredAt = t.CreatedAt, SecondaryDate = t.SubmissionDeadline,
                    DepartmentId = t.Requisition.DepartmentId, Department = t.Requisition.Department.Name,
                    PersonId = t.CreatedByUserId, Person = t.CreatedByUser.FullName,
                    Count = t.Invitations.Select(i => i.SupplierProfileId).Distinct().Count(),
                    SecondCount = t.Bids.Count(b => b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft
                        && b.Status != Statuses.BidStatus.Withdrawn)
                });
            case "admin-supplier-activity":
                return _db.SupplierProfiles.AsNoTracking().Select(s => new ReportRecordViewModel
                {
                    RecordId = s.Id.ToString(), SupplierId = s.Id, Supplier = s.CompanyName,
                    Reference = s.CompanyName, Title = s.Category, Detail = s.Country + " / " + s.City, Status = s.Status, OccurredAt = s.CreatedAt,
                    Count = s.Invitations.Where(i => (!from.HasValue || i.SentAt >= from.Value) && (!until.HasValue || i.SentAt < until.Value)).Select(i => i.TenderId).Distinct().Count(),
                    SecondCount = s.Bids.Where(b => b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft
                        && b.Status != Statuses.BidStatus.Withdrawn && (!from.HasValue || b.SubmittedAt >= from.Value) && (!until.HasValue || b.SubmittedAt < until.Value)).Select(b => b.TenderId).Distinct().Count(),
                    ReadyCount = !from.HasValue && !until.HasValue
                        ? _db.Tenders.Count(t => t.AwardedSupplierId == s.Id && t.Status == Statuses.TenderStatus.Awarded)
                        : _db.TenderApprovals.Where(a => a.Tender.AwardedSupplierId == s.Id && a.Tender.Status == Statuses.TenderStatus.Awarded
                            && a.Decision == Statuses.ApprovalDecision.Approved
                            && a.Level == (a.Tender.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager)
                            && (!from.HasValue || a.DecidedAt >= from.Value) && (!until.HasValue || a.DecidedAt < until.Value))
                            .Select(a => a.TenderId).Distinct().Count()
                });
            case "admin-documents":
                return _db.SupplierDocuments.AsNoTracking().Select(d => new ReportRecordViewModel
                {
                    RecordId = d.Id.ToString(), Reference = d.SupplierProfile.CompanyName, Title = d.DocType,
                    SupplierId = d.SupplierProfileId, Supplier = d.SupplierProfile.CompanyName,
                    Status = d.ExpiryDate == null ? "Expiry not recorded" : d.ExpiryDate < today ? "Expired"
                        : d.ExpiryDate <= today.AddDays(30) ? "Expires within 30 days" : "Not expired",
                    OccurredAt = d.UploadedAt, SecondaryDate = d.ExpiryDate, Detail = d.DocType
                });
            case "admin-offers":
                return _db.Bids.AsNoTracking().Select(b => new ReportRecordViewModel
                {
                    RecordId = b.Id.ToString(), Reference = "Offer #" + b.Id, Title = b.SupplierProfile.CompanyName,
                    SupplierId = b.SupplierProfileId, Supplier = b.SupplierProfile.CompanyName,
                    TenderNumber = b.Tender.TenderNumber, TenderStatus = b.Tender.Status,
                    DepartmentId = b.Tender.Requisition.DepartmentId, Department = b.Tender.Requisition.Department.Name,
                    Status = b.Status, Detail = b.Tender.Title, OccurredAt = b.SubmittedAt, Amount = b.TotalPrice, Currency = b.Currency,
                    Compliance = b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                        .Select(c => c.Result).FirstOrDefault() ?? "Not reviewed"
                });
            case "admin-invitations":
                return _db.Invitations.AsNoTracking().Select(i => new ReportRecordViewModel
                {
                    RecordId = i.Id.ToString(), Reference = i.Tender.TenderNumber, Title = i.SupplierProfile.CompanyName,
                    SupplierId = i.SupplierProfileId, Supplier = i.SupplierProfile.CompanyName,
                    TenderNumber = i.Tender.TenderNumber, TenderStatus = i.Tender.Status,
                    DepartmentId = i.Tender.Requisition.DepartmentId, Department = i.Tender.Requisition.Department.Name,
                    Status = i.Status, OccurredAt = i.SentAt, SecondaryDate = i.ExpiresAt, ThirdDate = i.RespondedAt,
                    Detail = i.SupplierProfile.CompanyName
                });
            case "admin-members":
                return _db.CommitteeMembers.AsNoTracking().Select(m => new ReportRecordViewModel
                {
                    RecordId = m.Id.ToString(), Reference = m.Tender.TenderNumber, Title = m.User.FullName, OccurredAt = null,
                    PersonId = m.UserId, Person = m.User.FullName, TenderNumber = m.Tender.TenderNumber,
                    TenderStatus = m.Tender.Status, DepartmentId = m.Tender.Requisition.DepartmentId,
                    Department = m.Tender.Requisition.Department.Name, Detail = m.User.FullName,
                    Status = _db.EvaluationCommitteeSubmissions.Any(s => s.TenderId == m.TenderId && s.EvaluatorUserId == m.UserId)
                        ? "Ready" : "Not Ready"
                });
            case "admin-evaluations":
                return _db.FinancialEvaluations.AsNoTracking().Select(e => new ReportRecordViewModel
                {
                    RecordId = e.Id.ToString(), Reference = "Offer #" + e.BidId, Title = e.Bid.SupplierProfile.CompanyName,
                    SupplierId = e.Bid.SupplierProfileId, Supplier = e.Bid.SupplierProfile.CompanyName,
                    TenderNumber = e.Bid.Tender.TenderNumber, TenderStatus = e.Bid.Tender.Status,
                    DepartmentId = e.Bid.Tender.Requisition.DepartmentId, Department = e.Bid.Tender.Requisition.Department.Name,
                    PersonId = e.EvaluatorUserId, Person = e.EvaluatorUser.FullName, Detail = e.EvaluatorUser.FullName,
                    Status = e.ReadyAt != null ? "Ready" : e.CompletedAt != null ? "Saved complete" : "Draft",
                    OccurredAt = e.EvaluatedAt
                });
            case "admin-reports":
                return _db.EvaluationReports.AsNoTracking().Select(r => new ReportRecordViewModel
                {
                    RecordId = r.Id.ToString(), Reference = r.Tender.TenderNumber, Title = "Report #" + r.Id,
                    TenderNumber = r.Tender.TenderNumber, TenderStatus = r.Tender.Status,
                    DepartmentId = r.Tender.Requisition.DepartmentId, Department = r.Tender.Requisition.Department.Name,
                    PersonId = r.PreparedByUserId, Person = r.PreparedByUser.FullName,
                    SupplierId = r.RecommendedBid == null ? null : r.RecommendedBid.SupplierProfileId,
                    Supplier = r.RecommendedBid == null ? "" : r.RecommendedBid.SupplierProfile.CompanyName,
                    Detail = r.PreparedByUser.FullName, Status = r.Status, OccurredAt = r.CreatedAt
                });
            case "admin-decisions":
                return _db.TenderApprovals.AsNoTracking().Select(a => new ReportRecordViewModel
                {
                    RecordId = a.Id.ToString(), Reference = a.Tender.TenderNumber, Title = a.Tender.Title,
                    TenderNumber = a.Tender.TenderNumber, TenderStatus = a.Tender.Status,
                    DepartmentId = a.Tender.Requisition.DepartmentId, Department = a.Tender.Requisition.Department.Name,
                    PersonId = a.DecidedByUserId, Person = a.DecidedByUser.FullName, DecisionLevel = a.Level,
                    SupplierId = a.SelectedBid == null ? null : a.SelectedBid.SupplierProfileId,
                    Supplier = a.SelectedBid == null ? "" : a.SelectedBid.SupplierProfile.CompanyName,
                    Detail = a.DecidedByUser.FullName, Status = a.Decision,
                    OccurredAt = a.DecidedAt, SecondaryDate = a.Report.SubmittedAt
                });
            case "admin-voting":
                return _db.TopCommitteeVotes.AsNoTracking().Select(v => new ReportRecordViewModel
                {
                    RecordId = v.Id.ToString(), Reference = v.Tender.TenderNumber, Title = v.MemberUser.FullName,
                    PersonId = v.MemberUserId, Person = v.MemberUser.FullName,
                    TenderNumber = v.Tender.TenderNumber, TenderStatus = v.Tender.Status,
                    DepartmentId = v.Tender.Requisition.DepartmentId, Department = v.Tender.Requisition.Department.Name,
                    Detail = v.MemberUser.FullName, Status = "Vote submitted", OccurredAt = v.VotedAt
                });
            case "admin-awards":
                return _db.Tenders.AsNoTracking().Where(t => t.Status == Statuses.TenderStatus.Awarded && t.AwardedSupplierId != null)
                    .Select(t => new ReportRecordViewModel {
                        RecordId = t.Id.ToString(), TenderId = t.Id, Reference = t.TenderNumber, TenderNumber = t.TenderNumber,
                        Title = t.Title, Status = t.Status, TenderStatus = t.Status, Detail = "Awarded supplier: " + t.AwardedSupplier!.CompanyName,
                        SupplierId = t.AwardedSupplierId, Supplier = t.AwardedSupplier!.CompanyName,
                        DepartmentId = t.Requisition.DepartmentId, Department = t.Requisition.Department.Name,
                        OccurredAt = t.TenderApprovals.Where(a => a.Decision == Statuses.ApprovalDecision.Approved
                            && a.Level == (t.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager))
                            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).Select(a => (DateTime?)a.DecidedAt).FirstOrDefault(),
                        Amount = t.TenderApprovals.Where(a => a.Decision == Statuses.ApprovalDecision.Approved
                            && a.Level == (t.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager)
                            && a.SelectedBid != null && a.SelectedBid.SupplierProfileId == t.AwardedSupplierId)
                            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).Select(a => (decimal?)a.SelectedBid!.TotalPrice).FirstOrDefault()
                            ?? t.Bids.Where(b => b.SupplierProfileId == t.AwardedSupplierId && b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
                            .OrderByDescending(b => b.SubmittedAt).ThenByDescending(b => b.Id).Select(b => (decimal?)b.TotalPrice).FirstOrDefault(),
                        Currency = t.TenderApprovals.Where(a => a.Decision == Statuses.ApprovalDecision.Approved
                            && a.Level == (t.RequiresTopCommittee ? Statuses.ApprovalLevel.TopCommittee : Statuses.ApprovalLevel.Manager)
                            && a.SelectedBid != null && a.SelectedBid.SupplierProfileId == t.AwardedSupplierId)
                            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).Select(a => a.SelectedBid!.Currency).FirstOrDefault()
                            ?? t.Bids.Where(b => b.SupplierProfileId == t.AwardedSupplierId && b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
                            .OrderByDescending(b => b.SubmittedAt).ThenByDescending(b => b.Id).Select(b => b.Currency).FirstOrDefault() ?? "Unspecified"
                    });
            default:
                throw new InvalidOperationException("Unknown Admin report type.");
        }
    }
}
