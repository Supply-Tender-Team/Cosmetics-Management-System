using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Knowledge;

namespace TenderSystem.Services.Web;

/// <summary>A page used for an answer, shown under it as a link. N is 1, 2, 3 and is written "W1", "W2" in the answer.</summary>
public sealed record WebSource(int N, string Title, string Url);

/// <summary>Text is the numbered passages for the prompt ("" when nothing was found); Notice is a short message for the user, or null.</summary>
public sealed record WebContext(string Text, List<WebSource> Sources, string? Notice);

/// <summary>
/// Web mode (AI-19) for ONE question: search the allowed websites, open the best pages (SafeWebFetcher), keep the passages that are most
/// similar to the question (the same embeddings as the company documents) and number them for the prompt.
/// Only the question text goes to the search service. The question is never written to the audit log.
/// </summary>
public sealed class WebAnswerService
{
    private const int PassagesPerAnswer = 4;
    private const int ChunksPerPage = 12;

    private static readonly RateLimiter WebLimit = new();

    private readonly WebOptions _options;
    private readonly WebDomainService _domains;
    private readonly WebSearchClient _search;
    private readonly SafeWebFetcher _fetcher;
    private readonly AiEmbeddingService _embedding;
    private readonly KnowledgeOptions _knowledge;
    private readonly IMemoryCache _cache;
    private readonly AuditService _audit;
    private readonly ILogger<WebAnswerService> _log;

    public WebAnswerService(IOptions<WebOptions> options, WebDomainService domains, WebSearchClient search, SafeWebFetcher fetcher,
        AiEmbeddingService embedding, IOptions<KnowledgeOptions> knowledge, IMemoryCache cache, AuditService audit, ILogger<WebAnswerService> log)
    {
        _options = options.Value;
        _domains = domains;
        _search = search;
        _fetcher = fetcher;
        _embedding = embedding;
        _knowledge = knowledge.Value;
        _cache = cache;
        _audit = audit;
        _log = log;
    }

    /// <summary>Web mode is on and has a key. (Whether any website is allowed is checked by <see cref="AvailableAsync"/>.)</summary>
    public bool Configured => _options.Configured;

    /// <summary>True when the "Web" switch may be shown: configured and at least one website allowed.</summary>
    public async Task<bool> AvailableAsync(CancellationToken ct) => Configured && (await _domains.NamesAsync(ct)).Count > 0;

    public async Task<WebContext> GetContextAsync(string userId, string question, CancellationToken ct)
    {
        if (!Configured) return new("", [], "Web mode is not available.");
        var names = await _domains.NamesAsync(ct);
        if (names.Count == 0) return new("", [], "No website is allowed for web mode yet.");
        if (!UnderLimit(userId)) return new("", [], "You are using web mode too fast. Please wait a moment.");

        var key = "web:" + string.Join(",", names) + "|" + question.Trim().ToLowerInvariant();
        if (_cache.TryGetValue(key, out WebContext? cached) && cached != null) return cached;

        List<WebResult> results;
        try { results = await _search.SearchAsync(question, names, ct); }
        catch (AiUnavailableException ex) { return new("", [], ex.Message); }

        // Whatever the service returns, only https pages of the allowed websites are used (the fetcher checks it again).
        var usable = results
            .Where(r => SafeWebFetcher.CheckAddress(r.Url, names, _options.AllowPrivateForTests) != null)
            .GroupBy(r => r.Url).Select(g => g.First())
            .Take(_options.MaxResults).ToList();
        if (usable.Count == 0) return Remember(key, new("", [], "Nothing was found on the allowed websites."));

        var fetched = await Task.WhenAll(usable.Take(_options.MaxPages).Select(async r => (Result: r, Fetch: await _fetcher.FetchAsync(r.Url, names, ct))));
        var pages = fetched.Where(f => f.Fetch.Page != null).Select(f => (f.Result, Page: f.Fetch.Page!)).ToList();

        var candidates = new List<(string Url, string Title, string Text)>();
        foreach (var (result, page) in pages)
            foreach (var piece in KnowledgeChunker.Split(page.Text, 250, 40).Take(ChunksPerPage))
                candidates.Add((page.Url, string.IsNullOrWhiteSpace(result.Title) ? page.Title : result.Title, piece));
        // a page that could not be opened still has the short text the search service showed
        foreach (var r in usable.Where(r => pages.All(p => p.Result.Url != r.Url) && r.Snippet.Length >= 40))
            candidates.Add((r.Url, r.Title, r.Snippet));

        var chosen = await RankAsync(question, candidates, ct);
        if (chosen.Count == 0) return Remember(key, new("", [], "Nothing useful was found on the allowed websites."));

        var sources = new List<WebSource>();
        var text = new StringBuilder();
        foreach (var (url, title, passage) in chosen)
        {
            var source = sources.FirstOrDefault(s => s.Url == url);
            if (source is null) { source = new WebSource(sources.Count + 1, title.Length == 0 ? new Uri(url).Host : title, url); sources.Add(source); }
            text.Append("[W").Append(source.N).Append("] ").Append(source.Title).Append(" (").Append(url).Append("): ").AppendLine(passage.Replace("\n", " "));
        }

        try { await _audit.LogAsync("AiWebSearch", "WebSearch", "web", $"{sources.Count} page(s): {string.Join(", ", sources.Select(s => new Uri(s.Url).Host).Distinct())}"); }   // never the question
        catch (Exception ex) { _log.LogWarning(ex, "Could not audit a web search"); }

        return Remember(key, new(text.ToString().Trim(), sources, null));
    }

    /// <summary>The passages most similar to the question. If the embedding model is not available, the first passage of each page is used.</summary>
    private async Task<List<(string Url, string Title, string Text)>> RankAsync(string question, List<(string Url, string Title, string Text)> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return [];
        try
        {
            var vectors = await _embedding.EmbedAsync([question, .. candidates.Select(c => c.Text)], ct);
            var ranked = candidates
                .Select((c, i) => (Item: c, Score: AiEmbeddingService.Dot(vectors[0], vectors[i + 1])))
                .Where(x => x.Score >= Math.Min(0.3, _knowledge.MinScore))
                .OrderByDescending(x => x.Score)
                .Take(PassagesPerAnswer)
                .Select(x => x.Item)
                .ToList();
            return ranked;
        }
        catch (AiUnavailableException ex)
        {
            _log.LogWarning(ex, "Web passages could not be ranked; using the start of each page");
            return candidates.GroupBy(c => c.Url).Select(g => g.First()).Take(PassagesPerAnswer).ToList();
        }
    }

    private WebContext Remember(string key, WebContext context)
    {
        _cache.Set(key, context, TimeSpan.FromMinutes(10));
        return context;
    }

    private bool UnderLimit(string userId) => WebLimit.TryTake(userId, _options.MaxPerMinute, TimeSpan.FromMinutes(1));
}
