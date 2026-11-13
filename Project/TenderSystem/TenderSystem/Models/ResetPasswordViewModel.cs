using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Models;

/// <summary>The "choose a new password" form opened from the email link.</summary>
public class ResetPasswordViewModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    /// <summary>Comes from the link in the email. Hidden field in the form.</summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please choose a new password.")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "The password must be at least 10 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please repeat the new password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
