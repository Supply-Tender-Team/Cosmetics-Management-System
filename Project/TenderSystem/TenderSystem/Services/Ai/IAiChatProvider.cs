namespace TenderSystem.Services.Ai;

/// <summary>One message of a conversation. Role is "system", "user" or "assistant".</summary>
public sealed record AiChatMessage(string Role, string Content);

/// <summary>Something that can answer a conversation: a local model (Ollama) or a hosted API.</summary>
public interface IAiChatProvider
{
    /// <summary>Sends the conversation and returns the text of the answer. Throws <see cref="AiUnavailableException"/> when the model cannot answer.</summary>
    Task<string> CompleteAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken);
}

/// <summary>The model could not be reached or refused. The message is safe to show to the user.</summary>
public sealed class AiUnavailableException : Exception
{
    public AiUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
