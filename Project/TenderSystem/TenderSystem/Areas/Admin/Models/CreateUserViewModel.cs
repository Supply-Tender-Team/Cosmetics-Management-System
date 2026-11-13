using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Admin.Models;

public class CreateUserViewModel
{
    [Required]
    [StringLength(150)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

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

    public List<CreateDepartmentOption> Departments { get; set; } = [];
    public List<CreateRoleOption> AvailableRoles { get; set; } = [];
}

public class CreateDepartmentOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class CreateRoleOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}
