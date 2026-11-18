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
            case "admin-no-offers":
                return CreateAdminQuery("admin-tenders", filter).Where(r => r.SecondCount == 0 && r.Status != Statuses.TenderStatus.Draft);
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
                    Title = t.Title, Detail = "", Status = t.Status, OccurredAt = t.CreatedAt, SecondaryDate = t.SubmissionDeadline,
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
                return CreateQuery("awards", "");
            default:
                throw new InvalidOperationException("Unknown Admin report type.");
        }
    }
}
