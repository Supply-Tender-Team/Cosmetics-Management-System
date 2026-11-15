using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models;

public sealed class ProfileViewModel
{
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
}

public sealed class EditMyProfileViewModel
{
    [Required, StringLength(150)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [RegularExpression(@"^\d{10}$", ErrorMessage = "Phone number must contain exactly 10 digits.")]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }
}
