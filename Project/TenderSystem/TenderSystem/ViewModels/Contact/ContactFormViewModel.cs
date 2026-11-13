using System.ComponentModel.DataAnnotations;

namespace TenderSystem.ViewModels.Contact;

public class ContactFormViewModel
{
    [Required, StringLength(150)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(200)]
    public string Subject { get; set; } = "";

    [Required, StringLength(4000, MinimumLength = 10)]
    public string Message { get; set; } = "";

    [StringLength(200)]
    public string? Website { get; set; }
}
