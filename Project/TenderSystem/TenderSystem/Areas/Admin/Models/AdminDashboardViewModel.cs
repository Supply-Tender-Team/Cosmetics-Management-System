namespace TenderSystem.Areas.Admin.Models;

/// <summary>Read-only numbers shown on the Admin Dashboard. Every value comes from the database.</summary>
public class AdminDashboardViewModel
{
    public int UsersTotal { get; set; }
    public int EmployeesTotal { get; set; }
    public int EmployeesActive { get; set; }
    public int EmployeesInactive => EmployeesTotal - EmployeesActive;

    public int DepartmentsTotal { get; set; }
    public int DepartmentsActive { get; set; }

    public int SuppliersTotal { get; set; }
    public int SuppliersApproved { get; set; }
    public int SuppliersPending { get; set; }

    public int PurchaseRequestsTotal { get; set; }
    public int TendersTotal { get; set; }
    public int TendersOpen { get; set; }

    public List<DashboardBarViewModel> EmployeesByRole { get; set; } = [];
    public List<DashboardBarViewModel> EmployeesByDepartment { get; set; } = [];
    public List<DashboardBarViewModel> SuppliersByStatus { get; set; } = [];

    // Contact Us inbox: counts and the newest few messages (same row type as the inbox page).
    public int ContactTotal { get; set; }
    public int ContactUnread { get; set; }
    public List<ContactMessageRow> RecentContactMessages { get; set; } = [];
}

/// <summary>One row of a breakdown: a label, its count and an optional link filter value.</summary>
public class DashboardBarViewModel
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string? FilterValue { get; set; }
}
