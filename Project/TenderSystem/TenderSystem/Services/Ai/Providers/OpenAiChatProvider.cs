using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Hosted model through the OpenAI "chat/completions" format. Many services speak it (OpenAI, Groq, OpenRouter,
/// Gemini's compatible endpoint...), so changing provider is only a change of AI_API_BASE_URL / AI_API_KEY / AI_API_MODEL.
/// The key stays on the server; the browser never sees it.
/// Two ways to ask: CompleteAsync (the whole answer at once) and StreamAsync (server-sent events: one "data: {json}" line per piece, then "data: [DONE]").
/// </summary>
public sealed class OpenAiChatProvider : IAiChatProvider
{
    private const string Unreachable = "Could not reach the AI service. Please try again later.";

    private readonly HttpClient _http;
    private readonly AiChatOptions _options;

    public OpenAiChatProvider(IHttpClientFactory factory, IOptions<AiChatOptions> options)
    {
        _http = factory.CreateClient("ai");
        _options = options.Value;
    }

    // ---------------------------------------------------------------- the whole answer at once

    public async Task<string> CompleteAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken, bool json = false)
    {
        using var request = NewRequest(messages, stream: false, json);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new AiUnavailableException(ErrorMessage((int)response.StatusCode));

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return AiThinkFilter.Strip(text);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException(Unreachable, ex);
        }
    }

    // ---------------------------------------------------------------- the answer piece by piece

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AiChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = NewRequest(messages, stream: true, json: false);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException(Unreachable, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new AiUnavailableException(ErrorMessage((int)response.StatusCode));

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
            var filter = new AiThinkFilter();
            while (true)
            {
                string? line;
                try { line = await reader.ReadLineAsync(cancellationToken); }
                catch (IOException ex) { throw new AiUnavailableException("The connection to the AI service was lost. Please try again.", ex); }

                if (line is null) break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;

                var visible = filter.Push(ReadPiece(data));
                if (visible.Length > 0) yield return visible;
            }
            var rest = filter.Finish();
            if (rest.Length > 0) yield return rest;
        }
    }

    private static string ReadPiece(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return "";
            return choices[0].TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content)
                ? content.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    // ---------------------------------------------------------------- what both ways share

    /// <summary>The request, with the key as a bearer token. No key means no call at all.</summary>
    private HttpRequestMessage NewRequest(IReadOnlyList<AiChatMessage> messages, bool stream, bool json)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiUnavailableException("The AI assistant is not configured yet (no API key). Ask the administrator.");

        var body = new Dictionary<string, object>
        {
            ["model"] = _options.ApiModel,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
        };
        if (stream) body["stream"] = true;
        if (json) body["response_format"] = new { type = "json_object" };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return request;
    }

    private static string ErrorMessage(int status) => status switch
    {
        401 or 403 => "The AI service refused our key. Ask the administrator to check AI_API_KEY.",
        429 => "The AI service is busy or over its limit. Please try again in a minute.",
        _ => "The AI service returned an error. Please try again later.",
    };
}
