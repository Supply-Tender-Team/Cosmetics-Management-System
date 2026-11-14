using Microsoft.AspNetCore.Identity;

namespace TenderSystem.Data.Entities.Identity;

/// <summary>Login account for employees and suppliers (extends ASP.NET Core Identity).</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>"Employee" or "Supplier".</summary>
    public string UserType { get; set; } = "Employee";

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string? JobTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    /// <summary>NotStarted, InProgress, Completed. At least one of fingerprint or face is required to reach Completed (both are allowed).</summary>
    public string OnboardingStatus { get; set; } = "NotStarted";

    /// <summary>When the user agreed to the biometric policy.</summary>
    public DateTime? BiometricConsentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
