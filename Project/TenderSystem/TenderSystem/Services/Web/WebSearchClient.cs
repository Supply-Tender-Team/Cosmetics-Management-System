using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TenderSystem.Services.Ai;

namespace TenderSystem.Services.Web;

public sealed record WebResult(string Title, string Url, string Snippet);

/// <summary>
/// Asks the search service (Brave Search API) for pages, limited to the Admin's allowed domains with "site:" terms.
/// The search key is sent only in a header, never logged and never shown. Only the question text and the domain names leave the company.
/// </summary>
public sealed class WebSearchClient
{
    private const int QueryBudget = 380;                         // the service refuses longer queries

    private readonly HttpClient _http;
    private readonly WebOptions _options;

    public WebSearchClient(IHttpClientFactory factory, IOptions<WebOptions> options)
    {
        _http = factory.CreateClient("ai");
        _options = options.Value;
    }

    /// <summary>The query: (site:a.org OR site:b.org) and the question. Returns also how many of the domains fitted in.</summary>
    public static (string Query, int DomainsUsed) BuildQuery(string question, IReadOnlyList<string> domains)
    {
        question = Regex.Replace(question ?? "", @"\s+", " ").Trim();
        if (question.Length > 150) question = question[..150];

        var sites = new List<string>();
        var length = 4;                                          // brackets and a space
        foreach (var d in domains)
        {
            var term = "site:" + d;
            var cost = term.Length + (sites.Count > 0 ? 4 : 0);  // " OR "
            if (length + cost + question.Length + 1 > QueryBudget) break;
            sites.Add(term);
            length += cost;
        }
        return ($"({string.Join(" OR ", sites)}) {question}", sites.Count);
    }

    /// <summary>Results in the service's order. Throws <see cref="AiUnavailableException"/> (message safe to show) when the search cannot be done.</summary>
    public async Task<List<WebResult>> SearchAsync(string question, IReadOnlyList<string> domains, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiUnavailableException("Web mode is not configured yet (no search key). Ask the administrator.");

        var (query, used) = BuildQuery(question, domains);
        if (used == 0) throw new AiUnavailableException("No website is allowed for web mode yet. Ask the administrator.");

        var url = $"{_options.BaseUrl}{(_options.BaseUrl.Contains('?') ? '&' : '?')}q={Uri.EscapeDataString(query)}&count={Math.Clamp(_options.MaxResults * 2, 3, 20)}&text_decorations=false&safesearch=moderate";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Subscription-Token", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiUnavailableException((int)response.StatusCode switch
                {
                    401 or 403 or 422 => "The web search service refused our key. Ask the administrator to check AI_WEB_SEARCH_API_KEY.",
                    429 => "The web search service is over its limit. Please try again later.",
                    _ => "The web search service returned an error. Please try again later.",
                });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var results = new List<WebResult>();
            if (doc.RootElement.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var link = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrWhiteSpace(link)) continue;
                    results.Add(new WebResult(
                        Clean(item.TryGetProperty("title", out var t) ? t.GetString() : "", 200),
                        link,
                        Clean(item.TryGetProperty("description", out var d) ? d.GetString() : "", 600)));
                }
            }
            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiUnavailableException("The web search took too long. Please try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new AiUnavailableException("Could not reach the web search service. Please try again later.", ex);
        }
    }

    private static string Clean(string? text, int max)
    {
        var value = Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(text ?? "", "<[^>]*>", " ")), @"\s+", " ").Trim();
        return value.Length > max ? value[..max] : value;
    }
}
