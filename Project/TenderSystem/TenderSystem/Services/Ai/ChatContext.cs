using System.Security.Claims;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Everything gathered for ONE question, step by step: who asks, what they asked, the earlier turns, the data the role may see, the
/// company-document passages and the web passages. At the end <see cref="AiChatService.BuildMessages"/> turns it into the conversation for the model.
/// </summary>
public sealed class ChatContext
{
    public required ClaimsPrincipal User { get; init; }
    public required string UserId { get; init; }
    /// <summary>The role from the signed-in user's claims ("" when the user has no AI role). Never from the browser.</summary>
    public required string Role { get; init; }
    public required string Question { get; init; }
    public string? Page { get; init; }
    public List<AiChatTurn> Earlier { get; init; } = [];

    /// <summary>Real data for this question, already limited to the user's role. Null when there is none.</summary>
    public AiDataResult? Data { get; set; }
    /// <summary>"Tender TND-001: title" when the page the user came from is about a record they may see.</summary>
    public string? ContextLabel { get; set; }
    /// <summary>Numbered passages of the company documents (full page only). Null = not searched, empty = nothing matched.</summary>
    public string? Sources { get; set; }
    /// <summary>Numbered passages of the allowed websites (web mode only). Null = web mode is off.</summary>
    public string? Web { get; set; }

    /// <summary>The conversation to send to the model. Filled by <see cref="AiChatService.BuildMessages"/>.</summary>
    public List<AiChatMessage> Messages { get; } = [];

    public AiTable? Table => Data?.Table;
}
