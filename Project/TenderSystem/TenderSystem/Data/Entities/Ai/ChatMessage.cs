using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Data.Entities.Ai;

/// <summary>One question or answer inside a <see cref="ChatSession"/> (AI-17). Role is "user" or "assistant".</summary>
// OWNER: Wadea  |  table: ChatMessages
public class ChatMessage
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    [StringLength(20)]
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>The table the assistant showed under this answer (JSON), so the page looks the same when the chat is opened again. Deleted with the chat.</summary>
    public string? TableJson { get; set; }

    /// <summary>Record numbers of that table, separated by commas. They let a follow-up such as "the second one" be understood.</summary>
    [StringLength(500)]
    public string? RecordNumbers { get; set; }

    /// <summary>The company documents the answer was based on (JSON list of number and title), shown under the answer (AI-18).</summary>
    public string? SourcesJson { get; set; }

    /// <summary>The owner's rating of this answer: 1 = thumbs up, -1 = thumbs down, null = none (AI-20).</summary>
    public int? Feedback { get; set; }

    /// <summary>True when the user pressed Stop, so the answer is only the part written until then.</summary>
    public bool Stopped { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ChatSession Session { get; set; } = null!;
}
