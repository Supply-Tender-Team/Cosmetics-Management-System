using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Hosted model through the OpenAI "chat/completions" format. Many services speak it (OpenAI, Groq, OpenRouter,
/// Gemini's compatible endpoint...), so changing provider is only a change of AI_API_BASE_URL / AI_API_KEY / AI_API_MODEL.
/// The key stays on the server; the browser never sees it.
/// </summary>
public sealed class OpenAiChatProvider : IAiChatProvider
{
    private readonly HttpClient _http;
    private readonly AiChatOptions _options;

    public OpenAiChatProvider(IHttpClientFactory factory, IOptions<AiChatOptions> options)
    {
        _http = factory.CreateClient("ai");
        _options = options.Value;
    }

    public async Task<string> CompleteAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiUnavailableException("The AI assistant is not configured yet (no API key). Ask the administrator.");

        var body = new
        {
            model = _options.ApiModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiUnavailableException((int)response.StatusCode switch
                {
                    401 or 403 => "The AI service refused our key. Ask the administrator to check AI_API_KEY.",
                    429 => "The AI service is busy or over its limit. Please try again in a minute.",
                    _ => "The AI service returned an error. Please try again later.",
                });
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var text = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return OllamaChatProvider.Clean(text);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("Could not reach the AI service. Please try again later.", ex);
        }
    }
}
