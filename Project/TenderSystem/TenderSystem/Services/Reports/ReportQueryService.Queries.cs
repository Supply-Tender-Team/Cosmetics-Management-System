using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Reports;

public partial class ReportQueryService
{
    // Each branch establishes ownership before any filter, count or export is applied.
    private IQueryable<ReportRecordViewModel> CreateQuery(string type, string userId)
    {
        switch (type)
        {
            case "users":
                return _db.Users.AsNoTracking().Select(u => new ReportRecordViewModel
                { RecordId = u.Id, Reference = u.FullName, Title = u.Email ?? "", Detail = "", Status = u.IsActive ? "Active" : "Inactive",
                    DepartmentId = u.DepartmentId, Department = u.Department == null ? "" : u.Department.Name, OccurredAt = u.CreatedAt, SecondaryDate = u.LastLoginAt });
            case "suppliers":
                return _db.SupplierProfiles.AsNoTracking().Select(s => new ReportRecordViewModel
                { RecordId = s.Id.ToString(), Reference = s.CompanyName, Title = s.Category, Detail = s.Country + " / " + s.City,
                    SupplierId = s.Id, Supplier = s.CompanyName, Status = s.Status, OccurredAt = s.CreatedAt });
            case "departments":
                return _db.Departments.AsNoTracking().Select(d => new ReportRecordViewModel
                { RecordId = d.Id.ToString(), Reference = d.Code, Title = d.Name, Detail = d.AspNetUsers.Count().ToString() + " accounts",
                    Status = d.IsActive ? "Active" : "Inactive", OccurredAt = null });
            case "audit":
                return _db.AuditLogs.AsNoTracking().GroupBy(a => new { Day = a.CreatedAt.Date, a.Action })
                    .Select(g => new ReportRecordViewModel { RecordId = g.Key.Action + g.Key.Day.ToString(), Reference = g.Key.Action,
                        Title = "Recorded activity", Detail = g.Count().ToString() + " event(s)", Status = g.Key.Action, OccurredAt = g.Key.Day });
            case "sign-ins":
                return _db.BiometricAuthLogs.AsNoTracking().GroupBy(a => new { Day = a.CreatedAt.Date, a.Method, a.Success })
                    .Select(g => new ReportRecordViewModel { RecordId = g.Key.Method + g.Key.Day.ToString() + g.Key.Success.ToString(),
                        Reference = g.Key.Method, Title = "Sign-in attempts", Detail = g.Count().ToString() + " attempt(s)",
                        Status = g.Key.Success ? "Succeeded" : "Failed", OccurredAt = g.Key.Day });
            case "my-requests":
            case "received-requests":
                var requests = _db.PurchaseRequisitions.AsNoTracking();
                requests = type == "my-requests" ? requests.Where(r => r.RequestedByUserId == userId)
                    : requests.Where(r => r.Status != Statuses.RequestStatus.Draft && r.SubmittedAt != null);
                return requests.Select(r => new ReportRecordViewModel { RecordId = r.Id.ToString(), Reference = r.RequestNumber,
                    Title = r.Title, Status = r.Status, OccurredAt = r.CreatedAt, Amount = r.EstimatedBudget, Currency = "Unspecified",
                    DepartmentId = r.DepartmentId, Department = r.Department.Name,
                    TenderNumber = _db.Tenders.Where(t => t.RequisitionId == r.Id && t.Status != Statuses.TenderStatus.Draft).OrderByDescending(t => t.CreatedAt).Select(t => t.TenderNumber).FirstOrDefault() ?? "",
                    Detail = r.Priority + " / " + (_db.Tenders.Where(t => t.RequisitionId == r.Id && t.Status != Statuses.TenderStatus.Draft).OrderByDescending(t => t.CreatedAt).Select(t => t.Status).FirstOrDefault() ?? "No published tender") });
            case "tenders":
                return _db.Tenders.AsNoTracking().Where(t => t.Status != Statuses.TenderStatus.Draft && t.PublishDate != null)
                    .Select(t => new ReportRecordViewModel { RecordId = t.Id.ToString(), Reference = t.TenderNumber, TenderNumber = t.TenderNumber,
                        Title = t.Title, Detail = "", Status = t.Status, OccurredAt = t.CreatedAt, SecondaryDate = t.SubmissionDeadline,
                        DepartmentId = t.Requisition.DepartmentId, Department = t.Requisition.Department.Name,
                        Count = t.Invitations.Count(), SecondCount = t.Bids.Count(b => b.Status != Statuses.BidStatus.Draft && b.SubmittedAt != null) });
            case "offers":
                return _db.Bids.AsNoTracking().Where(b => b.Status != Statuses.BidStatus.Draft && b.SubmittedAt != null && b.Tender.Status != Statuses.TenderStatus.Draft)
                    .Select(b => new ReportRecordViewModel { RecordId = b.Id.ToString(), Reference = "Offer #" + b.Id, Title = b.SupplierProfile.CompanyName,
                        TenderNumber = b.Tender.TenderNumber, DepartmentId = b.Tender.Requisition.DepartmentId, Department = b.Tender.Requisition.Department.Name,
                        SupplierId = b.SupplierProfileId, Supplier = b.SupplierProfile.CompanyName, Status = b.Status,
                        OccurredAt = b.SubmittedAt, Amount = b.TotalPrice, Currency = b.Currency,
                        Detail = b.ComplianceChecks.OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id).Select(c => c.Result).FirstOrDefault() ?? "Not reviewed" });
            case "evaluation":
            case "assigned-evaluation":
                var tenders = _db.Tenders.AsNoTracking().Where(t => t.Status != Statuses.TenderStatus.Draft && t.PublishDate != null);
                if (type == "assigned-evaluation") tenders = tenders.Where(t => t.CommitteeMembers.Any(m => m.UserId == userId));
                return tenders.Select(t => new ReportRecordViewModel { RecordId = t.Id.ToString(), Reference = t.TenderNumber, TenderNumber = t.TenderNumber,
                    Title = t.Title, Status = t.Status, OccurredAt = t.CreatedAt, DepartmentId = t.Requisition.DepartmentId, Department = t.Requisition.Department.Name,
                    Detail = t.EvaluationReports.OrderByDescending(r => r.CreatedAt).Select(r => r.SubmittedAt != null ? "Report submitted to Officer" : "Report in progress").FirstOrDefault() ?? "No completed report",
                    RequiredCount = t.Bids.Count(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted) * 4,
                    ReadyCount = t.Bids.Where(b => b.Status == Statuses.BidStatus.Compliant || b.Status == Statuses.BidStatus.Accepted)
                        .SelectMany(b => b.FinancialEvaluations).Count(e => e.ReadyAt != null && t.CommitteeMembers.Any(m => m.UserId == e.EvaluatorUserId)) });
            case "review-reports":
                return _db.EvaluationReports.AsNoTracking().Where(r => r.SubmittedAt != null
                    && (r.Tender.Status == Statuses.TenderStatus.AwaitingManagerReview || r.Tender.Status == Statuses.TenderStatus.AwaitingCommitteeDecision
                        || r.Tender.Status == Statuses.TenderStatus.Awarded || r.Tender.Status == Statuses.TenderStatus.Rejected
                        || r.TenderApprovals.Any(a => a.DecidedByUserId == userId && a.Level == Statuses.ApprovalLevel.Manager)))
                    .Select(r => new ReportRecordViewModel { RecordId = r.Id.ToString(), Reference = r.Tender.TenderNumber, TenderNumber = r.Tender.TenderNumber,
                        Title = r.Tender.Title, Detail = r.Tender.Requisition.RequestNumber + " / " + r.PreparedByUser.FullName,
                        Status = r.Status, OccurredAt = r.SubmittedAt, DepartmentId = r.Tender.Requisition.DepartmentId, Department = r.Tender.Requisition.Department.Name });
            case "manager-decisions":
            case "committee-decisions":
                var level = type == "manager-decisions" ? Statuses.ApprovalLevel.Manager : Statuses.ApprovalLevel.TopCommittee;
                return _db.TenderApprovals.AsNoTracking().Where(a => a.Level == level && a.DecidedByUserId == userId)
                    .Select(a => new ReportRecordViewModel { RecordId = a.Id.ToString(), Reference = a.Tender.TenderNumber, TenderNumber = a.Tender.TenderNumber,
                        Title = a.Tender.Title, Status = a.Decision, OccurredAt = a.DecidedAt, SecondaryDate = a.Report.SubmittedAt,
                        Detail = a.Level, DepartmentId = a.Tender.Requisition.DepartmentId, Department = a.Tender.Requisition.Department.Name });
            case "votes":
                return _db.TopCommitteeVotes.AsNoTracking().Where(v => v.MemberUserId == userId)
                    .Select(v => new ReportRecordViewModel { RecordId = v.Id.ToString(), Reference = v.Tender.TenderNumber, TenderNumber = v.Tender.TenderNumber,
                        Title = v.Tender.Title, Detail = v.Comment ?? "No comment", Status = v.Vote, OccurredAt = v.VotedAt });
            case "awards":
                return _db.Tenders.AsNoTracking().Where(t => t.Status == Statuses.TenderStatus.Awarded && t.AwardedSupplierId != null)
                    .Select(t => new ReportRecordViewModel { RecordId = t.Id.ToString(), Reference = t.TenderNumber, TenderNumber = t.TenderNumber,
                        Title = t.Title, Status = t.Status, OccurredAt = t.CreatedAt, Detail = "Awarded supplier: " + t.AwardedSupplier!.CompanyName,
                        SupplierId = t.AwardedSupplierId, Supplier = t.AwardedSupplier!.CompanyName,
                        DepartmentId = t.Requisition.DepartmentId, Department = t.Requisition.Department.Name,
                        Amount = t.Bids.Where(b => b.SupplierProfileId == t.AwardedSupplierId && b.Status != Statuses.BidStatus.Draft).OrderByDescending(b => b.SubmittedAt).Select(b => (decimal?)b.TotalPrice).FirstOrDefault(),
                        Currency = t.Bids.Where(b => b.SupplierProfileId == t.AwardedSupplierId && b.Status != Statuses.BidStatus.Draft).OrderByDescending(b => b.SubmittedAt).Select(b => b.Currency).FirstOrDefault() ?? "Unspecified" });
            case "my-offers":
                return _db.Bids.AsNoTracking().Where(b => b.SupplierProfile.UserId == userId)
                    .Select(b => new ReportRecordViewModel { RecordId = b.Id.ToString(), Reference = "Offer #" + b.Id, Title = b.Tender.Title,
                        TenderNumber = b.Tender.TenderNumber, Status = b.Status, OccurredAt = b.SubmittedAt, Amount = b.TotalPrice, Currency = b.Currency,
                        Detail = b.DeliveryDays.ToString() + " delivery days / " + b.ValidityDays.ToString() + " validity days" });
            case "my-invitations":
                return _db.Invitations.AsNoTracking().Where(i => i.SupplierProfile.UserId == userId)
                    .Select(i => new ReportRecordViewModel { RecordId = i.Id.ToString(), Reference = i.Tender.TenderNumber, Title = i.Tender.Title, Detail = "",
                        TenderNumber = i.Tender.TenderNumber, Status = i.Status, OccurredAt = i.SentAt, SecondaryDate = i.ExpiresAt, ThirdDate = i.RespondedAt });
            default: throw new InvalidOperationException("Unknown report type.");
        }
    }
}
