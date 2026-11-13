using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Admin.Models;

public class EditUserViewModel
{
    [Required]
    public string Id { get; set; } = "";
    [Required]
    public string ConcurrencyStamp { get; set; } = "";
    public string OriginalRoles { get; set; } = "";
    public string Email { get; set; } = "";

    [Required]
    [StringLength(150)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [StringLength(100)]
    [Display(Name = "Job Title")]
    public string? JobTitle { get; set; }

    [Phone]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Required]
    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    [Required]
    [Display(Name = "Role")]
    public string Role { get; set; } = "";

    public List<EditDepartmentOption> Departments { get; set; } = [];
    public List<EditRoleOption> AvailableRoles { get; set; } = [];
}

public class EditDepartmentOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public class EditRoleOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}
