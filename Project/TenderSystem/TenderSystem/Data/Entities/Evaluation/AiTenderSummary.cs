using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>
/// One AI summary of a whole tender (all eligible offers side by side) for one audience: the evaluation committee, the Sourcing Manager
/// or the Top Committee. An ASSISTANT ONLY: it never fills scores or the report. The newest row of a tender and audience is shown.
/// </summary>
// OWNER: Wadea  |  table: AiTenderSummaries  |  service: Services/Ai/AiTenderSummaryService.cs
public class AiTenderSummary
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    /// <summary>Allowed values: Committee, Manager, TopCommittee.</summary>
    [StringLength(20)]
    public string Audience { get; set; } = string.Empty;
    /// <summary>The validated answer of the model (overview, offers, points to check, leaning) as JSON.</summary>
    public string ResultJson { get; set; } = string.Empty;
    /// <summary>SHA-256 of the facts the model was given. When the facts change later, the summary is shown as outdated.</summary>
    [StringLength(64)]
    public string FactsHash { get; set; } = string.Empty;
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tender Tender { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
}
