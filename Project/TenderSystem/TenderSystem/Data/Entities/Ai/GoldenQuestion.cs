using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Ai;

/// <summary>
/// A test question an Admin saves to check that the assistant still works after documents, roles or models were changed (AI-20).
/// "Asked as role R, the search must find document D" (or, with no document, "must find nothing"), and optionally
/// "the answer must contain these words". The Admin page runs them all and shows pass or fail.
/// </summary>
// OWNER: Wadea  |  table: GoldenQuestions
public class GoldenQuestion
{
    public int Id { get; set; }

    [StringLength(500)]
    public string Question { get; set; } = string.Empty;

    /// <summary>The role the question is asked as (one of the roles that may use company documents).</summary>
    [StringLength(50)]
    public string Role { get; set; } = string.Empty;

    /// <summary>Title of the document the search must find. Empty = the search must find NOTHING for this role (for example a document of another role).</summary>
    [StringLength(200)]
    public string? ExpectedDocument { get; set; }

    /// <summary>Words that must all appear in the answer, separated by commas. Empty = the answer is not checked.</summary>
    [StringLength(300)]
    public string? MustContain { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastRunAt { get; set; }

    /// <summary>Result of the last run: true = passed, false = failed, null = never run.</summary>
    public bool? LastPassed { get; set; }

    /// <summary>Why it failed, or what was found. Never the text of the answer.</summary>
    [StringLength(500)]
    public string? LastDetail { get; set; }

    public ApplicationUser CreatedBy { get; set; } = null!;
}
