namespace TenderSystem.Areas.Admin.Models;

/// <summary>Read-only numbers shown on the Admin Dashboard. Every value comes from the database.</summary>
/// <remarks>
/// "Employee" on this page always means: an account of type Employee that does NOT have the Admin role.
/// Admin accounts are only counted in <see cref="UsersTotal"/>.
/// </remarks>
public class AdminDashboardViewModel
{
    // ---------- header ----------
    /// <summary>The current time in the server time zone (the one time zone used on this page).</summary>
    public DateTime LocalNow { get; set; }
    /// <summary>For example "UTC+03:00". Shown next to the date and in the chart caption.</summary>
    public string TimeZoneLabel { get; set; } = "";

    // ---------- KPI cards (always global, never filtered) ----------
    public int UsersTotal { get; set; }

    public int EmployeesActive { get; set; }
    public int EmployeesDisabled { get; set; }
    public int EmployeesTotal => EmployeesActive + EmployeesDisabled;

    public int DepartmentsTotal { get; set; }
    public int DepartmentsActive { get; set; }
    public int DepartmentsInactive => DepartmentsTotal - DepartmentsActive;

    public int SuppliersTotal { get; set; }
    public int SuppliersApproved { get; set; }
    public int SuppliersPending { get; set; }

    public int PurchaseRequestsTotal { get; set; }
    public int TendersTotal { get; set; }
    public int TendersOpen { get; set; }

    // ---------- breakdown cards ----------
    public List<DashboardBarViewModel> SuppliersByStatus { get; set; } = [];
    /// <summary>One row per ROLE ASSIGNMENT: an employee with two roles appears in both rows.</summary>
    public List<DashboardBarViewModel> EmployeesByRole { get; set; } = [];
    /// <summary>Active employees per department (top 6).</summary>
    public List<DashboardBarViewModel> EmployeesByDepartment { get; set; } = [];

    // ---------- needs attention ----------
    public List<DashboardAttentionItem> AttentionItems { get; set; } = [];

    // ---------- monthly activity (the only part that uses the filters) ----------
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? DepartmentId { get; set; }
    public string? Status { get; set; }
    public string Activity { get; set; } = "requests";
    public string ChartMode { get; set; } = "bars";
    public string? FilterError { get; set; }
    public List<DashboardFilterOption> DepartmentOptions { get; set; } = [];
    public List<string> StatusOptions { get; set; } = [];
    public List<DashboardMonthPoint> MonthlyActivity { get; set; } = [];
}

/// <summary>One row of a breakdown: a label, its count and an optional link filter value.</summary>
public class DashboardBarViewModel
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string? FilterValue { get; set; }
}

/// <summary>One line of the "Needs attention" card. It is only created when something really needs attention.</summary>
public class DashboardAttentionItem
{
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>The number in the small bubble, for example "3" or "5 / 4".</summary>
    public string CountText { get; set; } = "";
    public bool Urgent { get; set; }

    // Where the item links to (an existing Admin page). Status / RoleId are only filled when that page supports them.
    public string Controller { get; set; } = "";
    public string? Status { get; set; }
    public string? RoleId { get; set; }
}

public class DashboardFilterOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>One bar / point of the Monthly Activity chart.</summary>
public class DashboardMonthPoint
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}
