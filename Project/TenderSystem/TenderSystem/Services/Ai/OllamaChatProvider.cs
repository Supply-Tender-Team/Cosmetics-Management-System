using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>Local model through Ollama (POST /api/chat). Nothing leaves the company computer.</summary>
public sealed class OllamaChatProvider : IAiChatProvider
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    public OllamaChatProvider(IHttpClientFactory factory, IOptions<OllamaOptions> options)
    {
        _http = factory.CreateClient("ai");
        _options = options.Value;
    }

    public async Task<string> CompleteAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken, bool json = false)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _options.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            ["stream"] = false,
            ["think"] = false,                   // no hidden "thinking" text, just the answer
        };
        if (json) body["format"] = "json";       // Ollama then only produces valid JSON

        try
        {
            // A plain string body (with Content-Length) is accepted by every server and proxy; a streamed one is not.
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_options.BaseUrl.TrimEnd('/')}/api/chat", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AiUnavailableException(detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? $"The local model \"{_options.Model}\" is not installed. Ask the administrator to run: ollama pull {_options.Model}"
                    : "The local AI model returned an error. Please try again later.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var text = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
            return Clean(text);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("The local AI model is not running. Ask the administrator to start Ollama.", ex);
        }
    }

    /// <summary>The answer piece by piece (Ollama sends one JSON line per piece).</summary>
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AiChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _options.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            ["stream"] = true,
            ["think"] = false,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("The local AI model is not running. Ask the administrator to start Ollama.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AiUnavailableException(detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? $"The local model \"{_options.Model}\" is not installed. Ask the administrator to run: ollama pull {_options.Model}"
                    : "The local AI model returned an error. Please try again later.");
            }

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
            if (root.TryGetProperty("error", out _))
                throw new AiUnavailableException("The local AI model returned an error. Please try again later.");
            var piece = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
                ? content.GetString() ?? "" : "";
            return (piece, root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return ("", false);        // a broken line is skipped, the next one continues the answer
        }
    }

    /// <summary>Some models still print their reasoning inside think tags. Users should only see the answer.</summary>
    internal static string Clean(string text) =>
        Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
}
