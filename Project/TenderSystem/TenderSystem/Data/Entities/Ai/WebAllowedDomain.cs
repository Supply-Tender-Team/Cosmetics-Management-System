using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Ai;

/// <summary>
/// A website the full-page assistant may use in "web mode" (AI-19), for example "example.org". The domain and its subdomains are allowed;
/// nothing else on the internet is ever searched or opened. Only an Admin adds or removes them.
/// </summary>
// OWNER: Wadea  |  table: WebAllowedDomains
public class WebAllowedDomain
{
    public int Id { get; set; }

    /// <summary>Lower case, no "https://", no path, for example "unctad.org".</summary>
    [StringLength(200)]
    public string Domain { get; set; } = string.Empty;

    public string AddedById { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser AddedBy { get; set; } = null!;
}
