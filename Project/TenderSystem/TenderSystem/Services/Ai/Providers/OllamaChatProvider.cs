using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Local model through Ollama (POST /api/chat). Nothing leaves the company computer.
/// Two ways to ask: CompleteAsync (the whole answer at once) and StreamAsync (piece by piece, Ollama sends one JSON line per piece).
/// Every problem becomes an <see cref="AiUnavailableException"/> with a message the person can read.
/// </summary>
public sealed class OllamaChatProvider : IAiChatProvider
{
    private const string NotRunning = "The local AI model is not running. Ask the administrator to start Ollama.";
    private const string GeneralError = "The local AI model returned an error. Please try again later.";

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    public OllamaChatProvider(IHttpClientFactory factory, IOptions<OllamaOptions> options)
    {
        _http = factory.CreateClient("ai");
        _options = options.Value;
    }

    // ---------------------------------------------------------------- the whole answer at once

    public async Task<string> CompleteAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken, bool json = false)
    {
        try
        {
            using var response = await _http.PostAsync(ChatUrl, Body(messages, stream: false, json), cancellationToken);
            if (!response.IsSuccessStatusCode) throw await FailureAsync(response, cancellationToken);

            return ReadAnswer(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException(NotRunning, ex);
        }
    }

    /// <summary>The text of the answer, without any reasoning text. A reply that is not the shape Ollama promises is reported, not crashed on.</summary>
    private static string ReadAnswer(string reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            var text = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
            return AiThinkFilter.Strip(text);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AiUnavailableException("The local AI model gave an answer that could not be read. Please try again.", ex);
        }
    }

    // ---------------------------------------------------------------- the answer piece by piece

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AiChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl) { Content = Body(messages, stream: true, json: false) };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException(NotRunning, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw await FailureAsync(response, cancellationToken);

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
            var filter = new AiThinkFilter();
            while (true)
            {
                var line = await ReadLineAsync(reader, cancellationToken);
                if (line is null) break;
                if (line.Length == 0) continue;

                var (piece, done) = ReadPiece(line);
                var visible = filter.Push(piece);
                if (visible.Length > 0) yield return visible;
                if (done) break;
            }
            var rest = filter.Finish();
            if (rest.Length > 0) yield return rest;
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try { return await reader.ReadLineAsync(cancellationToken); }
        catch (IOException ex) { throw new AiUnavailableException("The connection to the local AI model was lost. Please try again.", ex); }
    }

    private static (string Piece, bool Done) ReadPiece(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _)) throw new AiUnavailableException(GeneralError);
            var piece = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
                ? content.GetString() ?? "" : "";
            return (piece, root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return ("", false);        // a broken line is skipped, the next one continues the answer
        }
    }

    // ---------------------------------------------------------------- what both ways share

    private string ChatUrl => $"{_options.BaseUrl.TrimEnd('/')}/api/chat";

    /// <summary>
    /// The request body. think = false: no hidden reasoning text, just the answer. keep_alive: the model stays loaded, so the next request does not wait for it.
    /// json = true: Ollama then only produces valid JSON.
    /// A plain string body (with Content-Length) is accepted by every server and proxy; a streamed one is not.
    /// </summary>
    private StringContent Body(IReadOnlyList<AiChatMessage> messages, bool stream, bool json)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _options.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            ["stream"] = stream,
            ["think"] = false,
            ["keep_alive"] = _options.KeepAliveValue,
        };
        if (json) body["format"] = "json";
        return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }

    /// <summary>The error for an answer that is not a success: "the model is not installed" (with the command to fix it), or a general one.</summary>
    private async Task<AiUnavailableException> FailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        return new AiUnavailableException(detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? $"The local model \"{_options.Model}\" is not installed. Ask the administrator to run: ollama pull {_options.Model}"
            : GeneralError);
    }
}
