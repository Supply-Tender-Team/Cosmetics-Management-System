using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierProfileEditViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Company name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Tax number")]
    public string TaxNumber { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Commercial registration number")]
    public string CommercialRegNumber { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Country { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Address { get; set; } = string.Empty;

    [Required, StringLength(150)]
    [Display(Name = "Contact person")]
    public string ContactPerson { get; set; } = string.Empty;

    [Required, StringLength(30)]
    [Display(Name = "Contact phone")]
    public string ContactPhone { get; set; } = string.Empty;

    [StringLength(200), Url]
    public string? Website { get; set; }

    [Required, StringLength(100)]
    public string Category { get; set; } = string.Empty;
}
