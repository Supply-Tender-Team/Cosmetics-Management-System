using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Admin.Models;
using TenderSystem.Areas.Committee.Services;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class DashboardController : Controller
{
    // OWNER: Dev 3
    //
    // Rules for this page (read-only: it only counts rows, it never changes data):
    //  - "Employee" = an account of type Employee WITHOUT the Admin role. Admin accounts are only in "Total Users".
    //  - Time zone: dates are shown and grouped in the server's local time zone. The database keeps UTC,
    //    so every date boundary is converted to UTC before it is used in a query.
    //  - The Monthly Activity filters change ONLY the chart. Every other number is global.
    private const int ExpiringSoonDays = 30;
    private const int MaxRangeMonths = 12;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture; // same number format as the view (1,234)

    private readonly ApplicationDbContext _db;
    private readonly CommitteeVotingService _committeeVoting;

    public DashboardController(ApplicationDbContext db, CommitteeVotingService committeeVoting)
    {
        _db = db;
        _committeeVoting = committeeVoting;
    }

    // GET: shows the dashboard. The filter form uses GET too, so the filters stay in the URL.
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? departmentId,
        string? status, string activity = "requests", string chartMode = "bars")
    {
        // Step 1: prepare the ViewModel with the current time in the server time zone.
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);
        var offset = TimeZoneInfo.Local.GetUtcOffset(nowUtc);
        string sign = "+";
        if (offset < TimeSpan.Zero) sign = "-";

        var model = new AdminDashboardViewModel
        {
            LocalNow = nowLocal,
            TimeZoneLabel = "UTC" + sign + offset.ToString(@"hh\:mm")
        };

        // Step 2: Admin accounts are not employees, so we need their ids first.
        var adminIds = await GetAdminUserIdsAsync();

        // Step 3: fill each part of the page.
        await LoadNumbersAsync(model, adminIds);                             // KPI cards and breakdown cards
        await LoadMonthlyActivityAsync(model, from, to, departmentId, status, activity, chartMode);
        await LoadAttentionAsync(model, adminIds, nowUtc, nowLocal);         // needs attention

        return View(model);
    }

    // ------------------------------------------------------------------ helpers for employees

    // The ids of all accounts that have the Admin role.
    private async Task<List<string>> GetAdminUserIdsAsync()
    {
        return await (from userRole in _db.UserRoles
                      join role in _db.Roles on userRole.RoleId equals role.Id
                      where role.Name == Roles.Admin
                      select userRole.UserId).ToListAsync();
    }

    // Employees = accounts of type Employee that are NOT Admins (Admins are only counted in "Total Users").
    // It returns an IQueryable: nothing runs yet, so each caller can add its own filter and the database does the work.
    private IQueryable<ApplicationUser> GetEmployees(List<string> adminIds)
    {
        return _db.Users.AsNoTracking()
            .Where(user => user.UserType == Statuses.UserTypes.Employee && !adminIds.Contains(user.Id));
    }

    // ------------------------------------------------------------------ KPI cards and breakdowns

    private async Task LoadNumbersAsync(AdminDashboardViewModel model, List<string> adminIds)
    {
        var employees = GetEmployees(adminIds);

        model.UsersTotal = await _db.Users.AsNoTracking().CountAsync();

        // One query gives both numbers, so Active + Disabled is always the Total.
        var employeesByState = await employees
            .GroupBy(user => user.IsActive)
            .Select(group => new { IsActive = group.Key, Count = group.Count() })
            .ToListAsync();
        model.EmployeesActive = employeesByState.Where(row => row.IsActive).Sum(row => row.Count);
        model.EmployeesDisabled = employeesByState.Where(row => !row.IsActive).Sum(row => row.Count);

        // The department list is short. It feeds the KPI card and the filter drop-down.
        var departments = await _db.Departments.AsNoTracking()
            .OrderBy(department => department.Name)
            .Select(department => new { department.Id, department.Name, department.IsActive })
            .ToListAsync();
        model.DepartmentsTotal = departments.Count;
        model.DepartmentsActive = departments.Count(department => department.IsActive);
        model.DepartmentOptions = departments
            .Select(department => new DashboardFilterOption { Id = department.Id, Name = department.Name }).ToList();

        // One query for all supplier statuses: it feeds the KPI card AND the supplier pipeline.
        model.SuppliersByStatus = await _db.SupplierProfiles.AsNoTracking()
            .GroupBy(supplier => supplier.Status)
            .Select(group => new DashboardBarViewModel { Label = group.Key, Count = group.Count() })
            .ToListAsync();
        model.SuppliersTotal = model.SuppliersByStatus.Sum(row => row.Count);
        model.SuppliersApproved = CountOf(model.SuppliersByStatus, Statuses.SupplierStatus.Approved);
        model.SuppliersPending = CountOf(model.SuppliersByStatus, Statuses.SupplierStatus.Pending);

        model.PurchaseRequestsTotal = await _db.PurchaseRequisitions.AsNoTracking().CountAsync();

        // One query for all tender statuses: total and "open for offers".
        var tendersByStatus = await _db.Tenders.AsNoTracking()
            .GroupBy(tender => tender.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();
        model.TendersTotal = tendersByStatus.Sum(row => row.Count);
        model.TendersOpen = tendersByStatus
            .Where(row => row.Status == Statuses.TenderStatus.Published || row.Status == Statuses.TenderStatus.Open)
            .Sum(row => row.Count);

        // Team by role: one row per role ASSIGNMENT (an employee with two roles is counted in both).
        model.EmployeesByRole = await (from userRole in _db.UserRoles.AsNoTracking()
                                       join employee in employees on userRole.UserId equals employee.Id
                                       join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                       group role by new { role.Id, role.Name } into roleGroup
                                       orderby roleGroup.Count() descending
                                       select new DashboardBarViewModel
                                       {
                                           Label = roleGroup.Key.Name ?? "",
                                           Count = roleGroup.Count(),
                                           FilterValue = roleGroup.Key.Id
                                       }).ToListAsync();
        foreach (var bar in model.EmployeesByRole) bar.Label = Roles.LabelFor(bar.Label); // friendly names

        // Largest departments: count the ACTIVE employees of each department and keep the 6 biggest.
        // Each employee has one department, so nobody is counted twice.
        model.EmployeesByDepartment = await _db.Departments.AsNoTracking()
            .Select(department => new DashboardBarViewModel
            {
                Label = department.Name,
                FilterValue = department.Id.ToString(),
                Count = department.AspNetUsers.Count(user => user.IsActive
                    && user.UserType == Statuses.UserTypes.Employee
                    && !adminIds.Contains(user.Id))
            })
            .Where(bar => bar.Count > 0)
            .OrderByDescending(bar => bar.Count).ThenBy(bar => bar.Label)
            .Take(6)
            .ToListAsync();
    }

    private static int CountOf(List<DashboardBarViewModel> rows, string label)
        => rows.Where(row => row.Label == label).Sum(row => row.Count);

    // ------------------------------------------------------------------ Monthly Activity (the only filtered part)

    private async Task LoadMonthlyActivityAsync(AdminDashboardViewModel model, DateTime? from, DateTime? to,
        int? departmentId, string? status, string activity, string chartMode)
    {
        // Step 1: check the filter values. A wrong value is ignored and an error message is shown.
        if (activity != "requests" && activity != "tenders")
        {
            activity = "requests";
            model.FilterError = "Choose Requests or Tenders.";
        }
        if (chartMode != "bars" && chartMode != "lines") chartMode = "bars";

        var requestStatuses = new[]
        {
            Statuses.RequestStatus.Draft, Statuses.RequestStatus.Submitted, Statuses.RequestStatus.UnderReview,
            Statuses.RequestStatus.Approved, Statuses.RequestStatus.Rejected, Statuses.RequestStatus.Returned,
            Statuses.RequestStatus.ConvertedToTender
        };
        var tenderStatuses = new[]
        {
            Statuses.TenderStatus.Draft, Statuses.TenderStatus.Published, Statuses.TenderStatus.Open,
            Statuses.TenderStatus.Closed, Statuses.TenderStatus.UnderEvaluation,
            Statuses.TenderStatus.AwaitingManagerReview, Statuses.TenderStatus.AwaitingCommitteeDecision,
            Statuses.TenderStatus.Awarded, Statuses.TenderStatus.Cancelled,
            Statuses.TenderStatus.Rejected, Statuses.TenderStatus.RetenderRequired
        };
        // The status drop-down depends on the selected data set.
        if (activity == "requests")
            model.StatusOptions = requestStatuses.ToList();
        else
            model.StatusOptions = tenderStatuses.ToList();

        if (!string.IsNullOrEmpty(status) && !model.StatusOptions.Contains(status))
        {
            model.FilterError = "Choose a status for the selected activity.";
            status = null;
        }
        var departmentIsUnknown = departmentId.HasValue && model.DepartmentOptions.All(department => department.Id != departmentId.Value);
        if (departmentIsUnknown || ModelState["departmentId"]?.Errors.Count > 0)
        {
            model.FilterError = "Choose an available department.";
            departmentId = null;
        }
        if (!IsDateRangeValid(from, to))
        {
            model.FilterError = $"Choose a valid date range: From cannot be after To, and the range can cover up to {MaxRangeMonths} months.";
            from = null;
            to = null;
        }

        model.Activity = activity;
        model.ChartMode = chartMode;
        model.From = from;
        model.To = to;
        model.DepartmentId = departmentId;
        model.Status = status;

        // Step 2: decide which months the chart shows (in local time).
        // Default: the last 6 months. With only "from": 6 months starting there. With only "to": the 6 months ending there.
        var thisMonth = StartOfMonth(model.LocalNow);
        DateTime firstMonth;
        DateTime lastMonth;
        if (from.HasValue && to.HasValue)
        {
            firstMonth = StartOfMonth(from.Value);
            lastMonth = StartOfMonth(to.Value);
        }
        else if (from.HasValue)
        {
            firstMonth = StartOfMonth(from.Value);
            lastMonth = firstMonth.AddMonths(5);
        }
        else if (to.HasValue)
        {
            lastMonth = StartOfMonth(to.Value);
            firstMonth = lastMonth.AddMonths(-5);
        }
        else
        {
            lastMonth = thisMonth;
            firstMonth = thisMonth.AddMonths(-5);
        }

        // Step 3: the exact local time window. "To" is inclusive: the window ends when the next day starts.
        var startLocal = from?.Date ?? firstMonth;
        var endLocal = to?.Date.AddDays(1) ?? lastMonth.AddMonths(1);
        var startUtc = LocalToUtc(startLocal);
        var endUtc = LocalToUtc(endLocal);

        // Step 4: read the creation time of every matching request or tender.
        List<DateTime> createdAtUtc = await GetCreatedTimesAsync(activity, startUtc, endUtc, departmentId, status);

        // Step 5: count per LOCAL month (a row created late on the 31st UTC may belong to the next local month).
        var countPerMonth = createdAtUtc
            .Select(UtcToLocal)
            .GroupBy(createdAt => StartOfMonth(createdAt))
            .ToDictionary(group => group.Key, group => group.Count());

        // Step 6: one point for every month in the range, also the months with no records (count 0).
        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            countPerMonth.TryGetValue(month, out var count);
            model.MonthlyActivity.Add(new DashboardMonthPoint
            {
                Label = month.ToString("MMM yyyy", Invariant),
                Count = count
            });
        }
    }

    // Reads only the creation time (UTC) of the requests or tenders inside the time window (at most about a year).
    // The status filter uses the CURRENT status: the chart counts when a record was created, not when it changed status.
    private async Task<List<DateTime>> GetCreatedTimesAsync(string activity, DateTime startUtc, DateTime endUtc,
        int? departmentId, string? status)
    {
        if (activity == "requests")
        {
            var requests = _db.PurchaseRequisitions.AsNoTracking()
                .Where(request => request.CreatedAt >= startUtc && request.CreatedAt < endUtc);
            if (departmentId.HasValue) requests = requests.Where(request => request.DepartmentId == departmentId.Value);
            if (!string.IsNullOrEmpty(status)) requests = requests.Where(request => request.Status == status);
            return await requests.Select(request => request.CreatedAt).ToListAsync();
        }

        var tenders = _db.Tenders.AsNoTracking()
            .Where(tender => tender.CreatedAt >= startUtc && tender.CreatedAt < endUtc);
        if (departmentId.HasValue) tenders = tenders.Where(tender => tender.Requisition.DepartmentId == departmentId.Value);
        if (!string.IsNullOrEmpty(status)) tenders = tenders.Where(tender => tender.Status == status);
        return await tenders.Select(tender => tender.CreatedAt).ToListAsync();
    }

    private bool IsDateRangeValid(DateTime? from, DateTime? to)
    {
        if (ModelState["from"]?.Errors.Count > 0 || ModelState["to"]?.Errors.Count > 0) return false; // not a date
        if (from.HasValue && (from.Value.Year < 2000 || from.Value.Year > 2100)) return false;
        if (to.HasValue && (to.Value.Year < 2000 || to.Value.Year > 2100)) return false;
        if (!from.HasValue || !to.HasValue) return true;
        if (from.Value.Date > to.Value.Date) return false;
        var months = (to.Value.Year - from.Value.Year) * 12 + to.Value.Month - from.Value.Month;
        return months < MaxRangeMonths;
    }

    private static DateTime StartOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private static DateTime UtcToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);

    private static DateTime LocalToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // At a daylight-saving change one local hour does not exist: move to the next valid hour.
        if (TimeZoneInfo.Local.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }

    // ------------------------------------------------------------------ Needs attention

    // An item is added only when its number is above zero (or, for the committee, when the count is not exactly four).
    // For each item the text uses the singular when the count is 1 and the plural otherwise.
    private async Task LoadAttentionAsync(AdminDashboardViewModel model, List<string> adminIds, DateTime nowUtc, DateTime nowLocal)
    {
        var items = model.AttentionItems;

        // 1. Supplier registrations waiting for a decision (the number was already loaded above).
        if (model.SuppliersPending > 0)
        {
            string countText = model.SuppliersPending.ToString("#,0", Invariant);
            string text = "1 supplier is waiting for approval.";
            if (model.SuppliersPending != 1) text = countText + " suppliers are waiting for approval.";

            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-hourglass-split", Title = "Supplier registrations to review", Urgent = true,
                Text = text, CountText = countText, Controller = "Suppliers", Status = Statuses.SupplierStatus.Pending
            });
        }

        // 2. Top Committee: voting needs exactly four eligible members. Same rule as the voting page (read-only call).
        var eligibleMembers = (await _committeeVoting.GetEligibleMemberIdsAsync()).Count;
        if (eligibleMembers != CommitteeVotingService.RequiredMembers)
        {
            var topCommitteeRoleId = await _db.Roles.AsNoTracking()
                .Where(role => role.Name == Roles.TopCommittee).Select(role => role.Id).FirstOrDefaultAsync();
            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-people-fill", Title = "Top Committee needs exactly 4 members", Urgent = true,
                Text = $"{eligibleMembers} eligible members found. Voting is closed until there are exactly {CommitteeVotingService.RequiredMembers}.",
                CountText = $"{eligibleMembers} / {CommitteeVotingService.RequiredMembers}", Controller = "Users", RoleId = topCommitteeRoleId
            });
        }

        // 3. Legal documents whose CURRENT decision is Pending, or that have no decision yet.
        //    (The current decision of a document is its newest DocumentVerifications row.)
        var documentsAwaiting = await _db.SupplierDocuments.AsNoTracking()
            .Select(document => _db.DocumentVerifications
                .Where(verification => verification.SupplierDocumentId == document.Id)
                .OrderByDescending(verification => verification.Id)
                .Select(verification => verification.Status)
                .FirstOrDefault())
            .CountAsync(currentStatus => currentStatus == null || currentStatus == Statuses.VerificationStatus.Pending);
        if (documentsAwaiting > 0)
        {
            string countText = documentsAwaiting.ToString("#,0", Invariant);
            string text = "1 document has no final decision. Open a supplier to review it.";
            if (documentsAwaiting != 1) text = countText + " documents have no final decision. Open a supplier to review them.";

            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-shield-exclamation", Title = "Legal documents awaiting verification",
                Text = text, CountText = countText, Controller = "Suppliers"
            });
        }

        // 4. Documents that are still valid today but expire within the next 30 days (already expired ones are not counted).
        var today = nowLocal.Date;
        var expiryLimit = today.AddDays(ExpiringSoonDays);
        var documentsExpiring = await _db.SupplierDocuments.AsNoTracking()
            .CountAsync(document => document.ExpiryDate != null && document.ExpiryDate >= today && document.ExpiryDate <= expiryLimit);
        if (documentsExpiring > 0)
        {
            string countText = documentsExpiring.ToString("#,0", Invariant);
            string text = "1 valid document expires soon.";
            if (documentsExpiring != 1) text = countText + " valid documents expire soon.";

            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-calendar-x", Title = $"Documents expiring within {ExpiringSoonDays} days",
                Text = text, CountText = countText, Controller = "Suppliers"
            });
        }

        // 5. Employees who are enabled but temporarily locked out (too many failed sign-ins). Disabled accounts are item 6.
        var now = new DateTimeOffset(nowUtc, TimeSpan.Zero);
        var lockedOut = await GetEmployees(adminIds)
            .CountAsync(user => user.IsActive && user.LockoutEnd > now);
        if (lockedOut > 0)
        {
            string countText = lockedOut.ToString("#,0", Invariant);
            string text = "1 enabled account is temporarily locked after failed sign-ins.";
            if (lockedOut != 1) text = countText + " enabled accounts are temporarily locked after failed sign-ins.";

            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-lock", Title = "Locked-out employee accounts",
                Text = text, CountText = countText, Controller = "Users"
            });
        }

        // 6. Disabled employees (the number was already loaded above).
        if (model.EmployeesDisabled > 0)
        {
            string countText = model.EmployeesDisabled.ToString("#,0", Invariant);
            string text = "1 account cannot sign in.";
            if (model.EmployeesDisabled != 1) text = countText + " accounts cannot sign in.";

            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-person-slash", Title = "Disabled employee accounts",
                Text = text, CountText = countText, Controller = "Users", Status = "Inactive"
            });
        }

        // 7. Inactive departments (the number was already loaded above).
        if (model.DepartmentsInactive > 0)
        {
            string countText = model.DepartmentsInactive.ToString("#,0", Invariant);
            string text = "1 department is switched off.";
            if (model.DepartmentsInactive != 1) text = countText + " departments are switched off.";

            items.Add(new DashboardAttentionItem
            {
                Icon = "bi-building-slash", Title = "Inactive departments",
                Text = text, CountText = countText, Controller = "Departments", Status = "Inactive"
            });
        }
    }
}
