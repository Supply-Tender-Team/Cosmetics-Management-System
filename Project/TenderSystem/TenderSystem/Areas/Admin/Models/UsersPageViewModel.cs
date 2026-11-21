namespace TenderSystem.Areas.Admin.Models;

public class UsersPageViewModel
{
    public string? Search { get; set; }
    public int? DepartmentId { get; set; }
    public string? RoleId { get; set; }
    public string Status { get; set; } = "All";
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int DepartmentCount { get; set; }
    public int FilteredCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public const int PageSize = 10;
    public List<UserDepartmentOption> Departments { get; set; } = [];
    public List<UserRoleOption> Roles { get; set; } = [];
    public List<UserRowViewModel> Users { get; set; } = [];
}

public class UserDepartmentOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class UserRoleOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class UserRowViewModel
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Department { get; set; }
    public string Roles { get; set; } = "";
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; }
    public bool CanManage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserDetailsViewModel
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? Department { get; set; }
    public string Roles { get; set; } = "";
    public string RoleLabels { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
