using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models;

public sealed class EmployeeOnboardingViewModel
{
    public HashSet<string> MissingFields { get; set; } = new(StringComparer.Ordinal);

    [Required, StringLength(150)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    [Display(Name = "Employee number")]
    public string EmployeeNumber { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    [Display(Name = "Hire date")]
    public DateTime HireDate { get; set; } = DateTime.Today;

    [Required, Phone, StringLength(30)]
    [Display(Name = "Work phone")]
    public string WorkPhone { get; set; } = string.Empty;

    [Required, RegularExpression("^(en|ar)$")]
    [Display(Name = "Preferred language")]
    public string PreferredLanguage { get; set; } = "en";
}
