using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models;

public sealed class SupplierOnboardingViewModel
{
    [Required, StringLength(150)]
    [Display(Name = "Your name")]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Company name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Tax number")]
    public string TaxNumber { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Commercial registration number")]
    public string CommercialRegNumber { get; set; } = string.Empty;

    [Required, StringLength(80)] public string Country { get; set; } = string.Empty;
    [Required, StringLength(80)] public string City { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Address { get; set; } = string.Empty;
    [Required, StringLength(150)] public string ContactPerson { get; set; } = string.Empty;
    [Required, Phone, StringLength(30)] public string ContactPhone { get; set; } = string.Empty;
    [Url, StringLength(200)] public string? Website { get; set; }
    [Required, StringLength(100)] public string Category { get; set; } = string.Empty;
}
