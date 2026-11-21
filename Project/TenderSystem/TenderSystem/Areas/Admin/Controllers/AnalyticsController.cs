using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class AnalyticsController : Controller
{
    private readonly ApplicationDbContext _db;

    public AnalyticsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? departmentId, string? status,
        CancellationToken ct)
    {
        string? error = null;
        if (!ModelState.IsValid || (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            || to?.Year >= 9999)
        {
            error = "Choose a valid date range and filter values.";
            from = null;
            to = null;
        }

        var departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new AnalyticsFilterOption { Id = d.Id, Name = d.Name }).ToListAsync(ct);
        if (departmentId.HasValue && departments.All(d => d.Id != departmentId.Value))
        {
            error = "Choose an available department.";
            departmentId = null;
        }

        var validStatuses = new[] {
            Statuses.RequestStatus.Draft, Statuses.RequestStatus.Submitted, Statuses.RequestStatus.UnderReview,
            Statuses.RequestStatus.Approved, Statuses.RequestStatus.Rejected, Statuses.RequestStatus.Returned,
            Statuses.RequestStatus.ConvertedToTender, Statuses.TenderStatus.Published, Statuses.TenderStatus.Open,
            Statuses.TenderStatus.Closed, Statuses.TenderStatus.UnderEvaluation,
            Statuses.TenderStatus.AwaitingManagerReview, Statuses.TenderStatus.AwaitingCommitteeDecision,
            Statuses.TenderStatus.Awarded, Statuses.TenderStatus.Cancelled,
            Statuses.BidStatus.Submitted, Statuses.BidStatus.UnderReview, Statuses.BidStatus.Compliant,
            Statuses.BidStatus.NonCompliant, Statuses.BidStatus.Accepted, Statuses.BidStatus.Withdrawn };
        if (!string.IsNullOrWhiteSpace(status) && !validStatuses.Contains(status))
        {
            error = "Choose an available status.";
            status = null;
        }

        var start = from?.Date;
        var end = to?.Date.AddDays(1);
        var requests = _db.PurchaseRequisitions.AsNoTracking().AsQueryable();
        var tenders = _db.Tenders.AsNoTracking().AsQueryable();
        var offers = _db.Bids.AsNoTracking().Where(b => b.SubmittedAt != null);
        var submittedReports = _db.EvaluationReports.AsNoTracking().Where(r => r.SubmittedAt != null);
        var draftReports = _db.EvaluationReports.AsNoTracking().Where(r => r.SubmittedAt == null);

        if (start.HasValue)
        {
            requests = requests.Where(r => r.CreatedAt >= start.Value);
            tenders = tenders.Where(t => t.CreatedAt >= start.Value);
            offers = offers.Where(b => b.SubmittedAt >= start.Value);
            submittedReports = submittedReports.Where(r => r.SubmittedAt >= start.Value);
            draftReports = draftReports.Where(r => r.CreatedAt >= start.Value);
        }
        if (end.HasValue)
        {
            requests = requests.Where(r => r.CreatedAt < end.Value);
            tenders = tenders.Where(t => t.CreatedAt < end.Value);
            offers = offers.Where(b => b.SubmittedAt < end.Value);
            submittedReports = submittedReports.Where(r => r.SubmittedAt < end.Value);
            draftReports = draftReports.Where(r => r.CreatedAt < end.Value);
        }
        if (departmentId.HasValue)
        {
            requests = requests.Where(r => r.DepartmentId == departmentId.Value);
            tenders = tenders.Where(t => t.Requisition.DepartmentId == departmentId.Value);
            offers = offers.Where(b => b.Tender.Requisition.DepartmentId == departmentId.Value);
            submittedReports = submittedReports.Where(r => r.Tender.Requisition.DepartmentId == departmentId.Value);
            draftReports = draftReports.Where(r => r.Tender.Requisition.DepartmentId == departmentId.Value);
        }
        if (!string.IsNullOrEmpty(status))
        {
            requests = requests.Where(r => r.Status == status);
            tenders = tenders.Where(t => t.Status == status);
            offers = offers.Where(b => b.Status == status);
        }

        var requestGroups = await requests.GroupBy(r => r.Status)
            .Select(g => new AnalyticsStatusRow { Label = g.Key, Count = g.Count() }).ToListAsync(ct);
        var tenderGroups = await tenders.GroupBy(t => t.Status)
            .Select(g => new AnalyticsStatusRow { Label = g.Key, Count = g.Count() }).ToListAsync(ct);
        var offerGroups = await offers.GroupBy(b => b.Status)
            .Select(g => new AnalyticsStatusRow { Label = g.Key, Count = g.Count() }).ToListAsync(ct);
        var departmentGroups = await requests.GroupBy(r => new { r.DepartmentId, r.Department.Name })
            .Select(g => new AnalyticsDepartmentRow { Id = g.Key.DepartmentId, Name = g.Key.Name, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Name).ToListAsync(ct);

        // Six calendar months are displayed; status represents each record's current state.
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-5);
        var requestMonths = await requests.Where(r => r.CreatedAt >= monthStart)
            .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.Status })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var tenderMonths = await tenders.Where(t => t.CreatedAt >= monthStart)
            .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month, t.Status })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var requestTrend = new List<AnalyticsTrendPoint>();
        var tenderTrend = new List<AnalyticsTrendPoint>();
        for (var i = 0; i < 6; i++)
        {
            var month = monthStart.AddMonths(i);
            requestTrend.Add(new AnalyticsTrendPoint
            {
                Label = month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Primary = requestMonths.Where(g => g.Year == month.Year && g.Month == month.Month
                    && g.Status == Statuses.RequestStatus.Submitted).Sum(g => g.Count),
                Secondary = requestMonths.Where(g => g.Year == month.Year && g.Month == month.Month
                    && g.Status == Statuses.RequestStatus.Approved).Sum(g => g.Count)
            });
            tenderTrend.Add(new AnalyticsTrendPoint
            {
                Label = month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Primary = tenderMonths.Where(g => g.Year == month.Year && g.Month == month.Month
                    && (g.Status == Statuses.TenderStatus.Published || g.Status == Statuses.TenderStatus.Open)).Sum(g => g.Count),
                Secondary = tenderMonths.Where(g => g.Year == month.Year && g.Month == month.Month
                    && g.Status == Statuses.TenderStatus.Awarded).Sum(g => g.Count)
            });
        }

        var supplierGroups = await _db.SupplierProfiles.AsNoTracking().GroupBy(s => s.Status)
            .Select(g => new AnalyticsStatusRow { Label = g.Key, Count = g.Count() }).ToListAsync(ct);
        int SupplierCount(string label) => supplierGroups.Where(g => g.Label == label).Sum(g => g.Count);

        var model = new AdminAnalyticsViewModel
        {
            From = from, To = to, DepartmentId = departmentId, Status = status, FilterError = error,
            Departments = departments, RequestsByStatus = requestGroups, TendersByStatus = tenderGroups,
            OffersByStatus = offerGroups, RequestsByDepartment = departmentGroups,
            RequestTrend = requestTrend, TenderTrend = tenderTrend,
            OpenTenders = tenderGroups.Where(g => g.Label == Statuses.TenderStatus.Published
                || g.Label == Statuses.TenderStatus.Open).Sum(g => g.Count),
            ClosedTenders = tenderGroups.Where(g => g.Label == Statuses.TenderStatus.Closed).Sum(g => g.Count),
            AwardedTenders = tenderGroups.Where(g => g.Label == Statuses.TenderStatus.Awarded).Sum(g => g.Count),
            TendersWithoutSubmittedOffers = await tenders.CountAsync(t => t.Status != Statuses.TenderStatus.Draft
                && !t.Bids.Any(b => b.SubmittedAt != null && b.Status != Statuses.BidStatus.Draft
                    && b.Status != Statuses.BidStatus.Withdrawn), ct),
            SubmittedEvaluationReports = await submittedReports.CountAsync(ct),
            ParticipatingSuppliers = await offers.Select(b => b.SupplierProfileId).Distinct().CountAsync(ct),
            ReportsAwaitingSubmission = await draftReports.CountAsync(ct),
            SuppliersTotal = supplierGroups.Sum(g => g.Count),
            SuppliersApproved = SupplierCount(Statuses.SupplierStatus.Approved),
            SuppliersPending = SupplierCount(Statuses.SupplierStatus.Pending),
            SuppliersRejected = SupplierCount(Statuses.SupplierStatus.Rejected),
            SuppliersSuspended = SupplierCount(Statuses.SupplierStatus.Suspended)
        };
        return View(model);
    }
}
