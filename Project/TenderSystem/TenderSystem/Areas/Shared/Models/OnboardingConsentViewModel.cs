using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models;

public sealed class OnboardingConsentViewModel
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Please confirm to continue.")]
    public bool Accepted { get; set; }
}
