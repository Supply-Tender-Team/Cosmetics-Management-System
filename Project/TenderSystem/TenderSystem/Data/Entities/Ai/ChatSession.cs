using System.ComponentModel.DataAnnotations;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Entities.Ai;

/// <summary>
/// One saved conversation of the full-page assistant (AI-17). It belongs to ONE user and nobody else can read it, not even an Admin:
/// every query filters by the signed-in user's id. Old conversations are deleted automatically (AI_CHAT_HISTORY_DAYS).
/// </summary>
// OWNER: Wadea  |  table: ChatSessions
public class ChatSession
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>The start of the first question, or the name the user typed.</summary>
    [StringLength(120)]
    public string Title { get; set; } = "New chat";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Time of the last message. The automatic deletion counts from here.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
    public List<ChatMessage> Messages { get; set; } = [];
}
