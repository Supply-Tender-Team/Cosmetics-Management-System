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

    public async Task<string> CompleteAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = _options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
            think = false,                       // no hidden "thinking" text, just the answer
        };

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

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var text = json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
            return Clean(text);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("The local AI model is not running. Ask the administrator to start Ollama.", ex);
        }
    }

    /// <summary>Some models still print their reasoning inside think tags. Users should only see the answer.</summary>
    internal static string Clean(string text) =>
        Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
}
