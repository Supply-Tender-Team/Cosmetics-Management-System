using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Data.Entities.Messaging;

/// <summary>
/// One email waiting to be sent, being sent, or already handled (EML-01). The caller puts the email here
/// (EmailOutboxService.EnqueueAsync) and a background worker sends it, so a failed or slow mail server never breaks the business action.
/// </summary>
// OWNER: Wadea  |  table: EmailOutbox
public class EmailOutboxMessage
{
    public int Id { get; set; }

    /// <summary>One value per email that must exist only once, for example "award-winner:42:7" (tender 42, supplier 7). Unique in the database.</summary>
    [StringLength(200)]
    public string DedupKey { get; set; } = string.Empty;

    /// <summary>What kind of email it is, for reports, for example "AwardWinner" or "AwardUnsuccessful".</summary>
    [StringLength(50)]
    public string Category { get; set; } = string.Empty;

    [StringLength(256)]
    public string ToEmail { get; set; } = string.Empty;

    [StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>The finished HTML. It is emptied after the email was sent, because it can hold commercial data.</summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>Pending, Sending, Sent or Failed.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>How many times sending was started.</summary>
    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    /// <summary>While a worker is sending: until when this row belongs to it. After that another worker may take it.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>The last problem, in words. Never holds the email text.</summary>
    [StringLength(500)]
    public string? LastError { get; set; }

    /// <summary>What the email is about, for reports, for example "Tender" and "42".</summary>
    [StringLength(30)]
    public string? RelatedEntity { get; set; }

    [StringLength(50)]
    public string? RelatedId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// How the email was handled when it got the status Sent: "smtp" (handed to the real mail server), "preview" (only saved as a file in
    /// App_Data/emails, nobody received it) or "off". Empty for rows sent before this column existed: unknown, never guessed.
    /// </summary>
    [StringLength(20)]
    public string? DeliveryMode { get; set; }
}
