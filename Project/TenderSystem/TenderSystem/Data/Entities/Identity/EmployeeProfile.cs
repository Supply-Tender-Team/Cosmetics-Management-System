using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TenderSystem.Data.Entities.Identity;

/// <summary>Extra data every employee fills during onboarding.</summary>
// OWNER: Abdallah  |  table: EmployeeProfiles  |  design: docs-site (Schema Diagram)
public class EmployeeProfile
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    [StringLength(30)]
    public string EmployeeNumber { get; set; } = string.Empty;
    [Column(TypeName = "date")]
    public DateTime HireDate { get; set; }
    [StringLength(30)]
    public string WorkPhone { get; set; } = string.Empty;
    [StringLength(10)]
    public string PreferredLanguage { get; set; } = "en";
    [StringLength(300)]
    public string? ProfilePhotoPath { get; set; }
    [StringLength(300)]
    public string? SignaturePath { get; set; }
    public DateTime? OnboardingCompletedAt { get; set; }

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
