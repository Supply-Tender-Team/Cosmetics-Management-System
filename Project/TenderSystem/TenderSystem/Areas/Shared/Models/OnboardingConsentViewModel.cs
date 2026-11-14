using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models;

public sealed class OnboardingConsentViewModel
{
    // No [Range] on a bool: the browser validator cannot compare a checkbox with "true" and blocks a ticked box.
    // The box is required by the browser (required rule) and checked again on the server (OnboardingController.Complete).
    public bool Accepted { get; set; }
}
