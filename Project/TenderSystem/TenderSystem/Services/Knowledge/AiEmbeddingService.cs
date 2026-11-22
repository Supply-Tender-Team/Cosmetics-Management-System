using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Ai;

namespace TenderSystem.Services.Knowledge;

/// <summary>
/// Turns text into an embedding: a list of numbers that stands for its meaning, so two texts with a similar meaning get similar numbers
/// (in any language the model knows). Uses the LOCAL Ollama model AI_KB_EMBED_MODEL (POST /api/embed), so company text never leaves this computer.
/// Every vector is scaled to length 1, which makes the dot product of two vectors their cosine similarity.
/// </summary>
public sealed class AiEmbeddingService
{
    private const int BatchSize = 16;

    private readonly HttpClient _http;
    private readonly OllamaOptions _ollama;
    private readonly KnowledgeOptions _options;

    public AiEmbeddingService(IHttpClientFactory factory, IOptions<OllamaOptions> ollama, IOptions<KnowledgeOptions> options)
    {
        _http = factory.CreateClient("ai");
        _ollama = ollama.Value;
        _options = options.Value;
    }

    public string Model => _options.EmbedModel;

    /// <summary>One vector per text, in the same order. Throws <see cref="AiUnavailableException"/> (message safe to show) when the model cannot answer.</summary>
    public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var result = new List<float[]>(texts.Count);
        for (var start = 0; start < texts.Count; start += BatchSize)
        {
            var batch = texts.Skip(start).Take(BatchSize).ToList();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.EmbedTimeoutSeconds));
            try
            {
                result.AddRange(await EmbedBatchAsync(batch, timeout.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiUnavailableException("The embedding model took too long to answer. Please try again.");
            }
        }
        return result;
    }

    private async Task<List<float[]>> EmbedBatchAsync(List<string> batch, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { model = _options.EmbedModel, input = batch });
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_ollama.BaseUrl.TrimEnd('/')}/api/embed", content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiUnavailableException(text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? $"The embedding model \"{_options.EmbedModel}\" is not installed. Run: ollama pull {_options.EmbedModel}"
                    : "The embedding model returned an error. Please try again later.");
            }

            using var doc = JsonDocument.Parse(text);
            var vectors = new List<float[]>();
            foreach (var item in doc.RootElement.GetProperty("embeddings").EnumerateArray())
                vectors.Add(Normalize(item.EnumerateArray().Select(x => x.GetSingle()).ToArray()));
            if (vectors.Count != batch.Count || vectors.Any(v => v.Length == 0))
                throw new AiUnavailableException("The embedding model gave an unexpected answer.");
            return vectors;
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("The local AI model is not running. Ask the administrator to start Ollama.", ex);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AiUnavailableException("The embedding model gave an unexpected answer.", ex);
        }
    }

    /// <summary>Scales a vector to length 1. An all-zero vector stays empty (it can never match anything).</summary>
    public static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var x in vector) sum += (double)x * x;
        if (sum <= 0) return [];
        var length = (float)Math.Sqrt(sum);
        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++) result[i] = vector[i] / length;
        return result;
    }

    /// <summary>Cosine similarity of two vectors of length 1 (they must have the same size).</summary>
    public static double Dot(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += (double)a[i] * b[i];
        return sum;
    }

    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, vector.Length * sizeof(float));
        return vector;
    }
}
