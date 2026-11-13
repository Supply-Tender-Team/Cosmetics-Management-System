using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Models;

/// <summary>The "forgot password" form: just the email address.</summary>
public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "This is not a valid email address.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
