namespace TenderSystem.Areas.Admin.Models;

/// <summary>Read-only Admin analytics. Dates are UTC; people and supplier totals are all-time.</summary>
public class AdminAnalyticsViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? DepartmentId { get; set; }
    public string? Status { get; set; }
    public string? FilterError { get; set; }
    public List<AnalyticsFilterOption> Departments { get; set; } = [];
    public List<AnalyticsStatusRow> RequestsByStatus { get; set; } = [];
    public List<AnalyticsStatusRow> TendersByStatus { get; set; } = [];
    public List<AnalyticsStatusRow> OffersByStatus { get; set; } = [];
    public List<AnalyticsDepartmentRow> RequestsByDepartment { get; set; } = [];
    public List<AnalyticsTrendPoint> RequestTrend { get; set; } = [];
    public List<AnalyticsTrendPoint> TenderTrend { get; set; } = [];
    public int PurchaseRequestsTotal => RequestsByStatus.Sum(x => x.Count);
    public int TendersTotal => TendersByStatus.Sum(x => x.Count);
    public int OffersTotal => OffersByStatus.Sum(x => x.Count);
    public int OpenTenders { get; set; }
    public int ClosedTenders { get; set; }
    public int AwardedTenders { get; set; }
    public int TendersWithoutSubmittedOffers { get; set; }
    public int ReportsAwaitingSubmission { get; set; }
    public int SubmittedEvaluationReports { get; set; }
    public int ParticipatingSuppliers { get; set; }
    public int SuppliersTotal { get; set; }
    public int SuppliersApproved { get; set; }
    public int SuppliersPending { get; set; }
    public int SuppliersRejected { get; set; }
    public int SuppliersSuspended { get; set; }
}

public class AnalyticsFilterOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AnalyticsStatusRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class AnalyticsDepartmentRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class AnalyticsTrendPoint
{
    public string Label { get; set; } = "";
    public int Primary { get; set; }
    public int Secondary { get; set; }
}
