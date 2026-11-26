using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Ai;

/// <summary>
/// The last AI result a user generated, kept so a refresh or a later visit shows it again without calling the model (AI-24).
/// One row per user, kind and key: generating again replaces it. Kinds: "advisor" (key = tender id, the Evaluation offer advisor) and
/// "report" (key = the report type, the Admin AI report). The tender summary and the bid analysis have their own tables.
/// The text is only what the page already showed to that user; nothing here is used to decide anything.
/// </summary>
// OWNER: Wadea  |  table: AiSavedResults
public class AiSavedResult
{
    public int Id { get; set; }

    /// <summary>"advisor" or "report".</summary>
    [StringLength(30)]
    public string Kind { get; set; } = string.Empty;

    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>advisor: the tender id. report: the report type.</summary>
    [StringLength(200)]
    public string Key { get; set; } = string.Empty;

    /// <summary>report: the id used by the PDF link (a new one for every generated report).</summary>
    [StringLength(40)]
    public string Token { get; set; } = string.Empty;

    /// <summary>advisor: a fingerprint of the facts it saw, so the page can say "outdated" when the offers changed.</summary>
    [StringLength(64)]
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The result as JSON (the view model the page shows).</summary>
    public string ResultJson { get; set; } = string.Empty;

    [StringLength(200)]
    public string Model { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser OwnerUser { get; set; } = null!;
}
