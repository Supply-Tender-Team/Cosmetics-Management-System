using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Models;

/// <summary>Fields of the supplier self-registration form.</summary>
public class RegisterSupplierViewModel
{
    public static readonly string[] Categories =
        ["Packaging", "Raw materials", "Fragrance & essential oils", "Equipment & machinery", "Logistics", "Other"];

    [Required, StringLength(200)]
    [Display(Name = "Company name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Tax number")]
    public string TaxNumber { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Commercial registration number")]
    public string CommercialRegNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Category")]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Country { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Address { get; set; } = string.Empty;

    [StringLength(200)]
    [Url(ErrorMessage = "Enter a full web address, for example https://example.com")]
    public string? Website { get; set; }

    [Required, StringLength(150)]
    [Display(Name = "Contact person")]
    public string ContactPerson { get; set; } = string.Empty;

    [Required, StringLength(30)]
    [Phone]
    [Display(Name = "Contact phone")]
    public string ContactPhone { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email (this is your login name)")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 10, ErrorMessage = "The password must be at least 10 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms to register.")]
    [Display(Name = "I accept the terms of use")]
    public bool AcceptTerms { get; set; }
}
