using DotNetEnv;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Loads the local model into memory when the application starts, so the first question or the first award email does not wait for it
/// (Ollama needs 3 to 30 seconds, more when the graphics card is busy, to load a model it unloaded after it was idle).
/// Together with OLLAMA_KEEP_ALIVE (every request tells Ollama how long to keep the model loaded) the model stays ready while the application runs.
/// It runs in the background, never blocks the start, and a failure (Ollama not running) is only logged. AI_WARMUP=false switches it off.
/// </summary>
public sealed class AiWarmupService : BackgroundService
{
    private readonly IHttpClientFactory _http;
    private readonly OllamaOptions _ollama;
    private readonly AiChatOptions _chat;
    private readonly AiAnalysisOptions _analysis;
    private readonly AiGenerationOptions _generation;
    private readonly ILogger<AiWarmupService> _log;

    public AiWarmupService(IHttpClientFactory http, IOptions<OllamaOptions> ollama, IOptions<AiChatOptions> chat,
        IOptions<AiAnalysisOptions> analysis, IOptions<AiGenerationOptions> generation, ILogger<AiWarmupService> log)
    {
        _http = http;
        _ollama = ollama.Value;
        _chat = chat.Value;
        _analysis = analysis.Value;
        _generation = generation.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!Env.GetBool("AI_WARMUP", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(15), stop); }       // let the application finish starting first
        catch (OperationCanceledException) { return; }

        // The models people wait for: the chat / analysis model and a dedicated local model of the analysis cards. The small email model loads in seconds, so it is not kept in memory.
        var models = new List<string>();
        if ((_chat.Provider == "ollama" || _analysis.Provider == "ollama") && !string.IsNullOrWhiteSpace(_ollama.Model)) models.Add(_ollama.Model);
        if (_generation.Insights is { IsDedicated: true, Provider: "ollama" } insights && !models.Contains(insights.Model)) models.Add(insights.Model);

        foreach (var model in models)
        {
            if (stop.IsCancellationRequested) return;
            await LoadAsync(model, stop);
        }
    }

    private async Task LoadAsync(string model, CancellationToken stop)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromMinutes(3));
            // /api/generate without a prompt only loads the model and keeps it for keep_alive.
            var body = JsonSerializer.Serialize(new { model, keep_alive = _ollama.KeepAliveValue });
            using var response = await _http.CreateClient("ai").PostAsync($"{_ollama.BaseUrl.TrimEnd('/')}/api/generate",
                new StringContent(body, Encoding.UTF8, "application/json"), limit.Token);
            if (response.IsSuccessStatusCode) _log.LogInformation("AI warm-up: the local model {Model} is loaded and stays ready ({KeepAlive}).", model, _ollama.KeepAlive);
            else _log.LogInformation("AI warm-up: the local model {Model} could not be loaded (HTTP {Status}).", model, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
        {
            _log.LogInformation("AI warm-up skipped for {Model}: {Message}", model, ex.Message);
        }
    }
}
