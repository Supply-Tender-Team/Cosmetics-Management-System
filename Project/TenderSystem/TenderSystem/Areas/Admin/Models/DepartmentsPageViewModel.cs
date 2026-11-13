namespace TenderSystem.Areas.Admin.Models;

public class DepartmentsPageViewModel
{
    public string? Search { get; set; }
    public string Status { get; set; } = "All";
    public List<DepartmentRowViewModel> Departments { get; set; } = [];
    public DepartmentFormViewModel Form { get; set; } = new();
    public bool OpenForm { get; set; }
}

public class DepartmentRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool IsActive { get; set; }
    public int MembersCount { get; set; }
}
