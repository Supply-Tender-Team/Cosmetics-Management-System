using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

/// <summary>A pretend model server: the tests say what it answers, and read what the provider sent to it.</summary>
internal sealed class FakeLlmServer : IHttpClientFactory
{
    public List<(string Url, string Body, string? Authorization)> Requests { get; } = [];
    public Func<HttpResponseMessage> Respond { get; set; } = () => Json("{}");

    public HttpClient CreateClient(string name) => new(new Handler(this));

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A streamed answer: one line per piece.</summary>
    public static HttpResponseMessage Lines(params string[] lines) =>
        new(HttpStatusCode.OK) { Content = new StringContent(string.Join("\n", lines) + "\n", Encoding.UTF8, "application/x-ndjson") };

    private sealed class Handler(FakeLlmServer server) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            server.Requests.Add((request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            return server.Respond();
        }
    }
}

/// <summary>What the two providers send, what they accept, and which message the person sees when the model cannot answer.</summary>
public class ProviderTests
{
    private static readonly AiChatMessage[] Chat = [new("system", "rules"), new("user", "hello")];

    private static OllamaChatProvider Ollama(FakeLlmServer server, Action<OllamaOptions>? tweak = null)
    {
        var options = new OllamaOptions { BaseUrl = "http://model:11434/", Model = "qwen-test", KeepAlive = "24h" };
        tweak?.Invoke(options);
        return new OllamaChatProvider(server, Options.Create(options));
    }

    private static OpenAiChatProvider OpenAi(FakeLlmServer server, string key = "secret-key") =>
        new(server, Options.Create(new AiChatOptions { Provider = "openai", ApiBaseUrl = "https://api.example.com/v1", ApiKey = key, ApiModel = "gpt-test" }));

    private static string OllamaAnswer(string text) => JsonSerializer.Serialize(new { message = new { role = "assistant", content = text }, done = true });
    private static string OllamaPiece(string text, bool done = false) => JsonSerializer.Serialize(new { message = new { role = "assistant", content = text }, done });
    private static string OpenAiAnswer(string text) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text } } } });
    private static string OpenAiPiece(string text) => "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = text } } } });

    private static async Task<List<string>> ReadAll(IAsyncEnumerable<string> pieces)
    {
        var all = new List<string>();
        await foreach (var p in pieces) all.Add(p);
        return all;
    }

    // ================================================================ Ollama (the local model)

    [Fact]
    public async Task Ollama_sends_the_model_the_conversation_and_asks_to_keep_the_model_loaded()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OllamaAnswer("Hi there")) };

        var answer = await Ollama(server).CompleteAsync(Chat, CancellationToken.None);

        Assert.Equal("Hi there", answer);
        var (url, body, _) = Assert.Single(server.Requests);
        Assert.Equal("http://model:11434/api/chat", url);                       // the trailing slash of the setting is handled
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("qwen-test", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal("24h", doc.RootElement.GetProperty("keep_alive").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("messages").GetArrayLength());
        Assert.False(doc.RootElement.TryGetProperty("format", out _));        // plain text unless JSON was asked for
    }

    [Fact]
    public async Task Ollama_asks_for_JSON_only_when_the_caller_wants_JSON()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OllamaAnswer("{}")) };

        await Ollama(server).CompleteAsync(Chat, CancellationToken.None, json: true);

        using var doc = JsonDocument.Parse(server.Requests[0].Body);
        Assert.Equal("json", doc.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Ollama_keep_alive_is_a_number_when_the_setting_is_a_number()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OllamaAnswer("ok")) };

        await Ollama(server, o => o.KeepAlive = "-1").CompleteAsync(Chat, CancellationToken.None);

        using var doc = JsonDocument.Parse(server.Requests[0].Body);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("keep_alive").ValueKind);
    }

    [Fact]
    public async Task Ollama_reasoning_text_is_removed_from_the_answer()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OllamaAnswer("<think>let me think</think>The answer is 4.")) };

        Assert.Equal("The answer is 4.", await Ollama(server).CompleteAsync(Chat, CancellationToken.None));
    }

    [Fact]
    public async Task Ollama_tells_the_administrator_when_the_model_is_not_installed()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json("{\"error\":\"model 'qwen-test' not found\"}", HttpStatusCode.NotFound) };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ollama(server).CompleteAsync(Chat, CancellationToken.None));

        Assert.Contains("ollama pull qwen-test", ex.Message);
    }

    [Fact]
    public async Task Ollama_gives_a_general_message_for_any_other_error()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json("{\"error\":\"boom\"}", HttpStatusCode.InternalServerError) };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ollama(server).CompleteAsync(Chat, CancellationToken.None));

        Assert.Contains("returned an error", ex.Message);
        Assert.DoesNotContain("boom", ex.Message);                              // the raw server text is not shown to the person
    }

    [Fact]
    public async Task Ollama_says_so_when_it_is_not_running()
    {
        var server = new FakeLlmServer { Respond = () => throw new HttpRequestException("connection refused") };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ollama(server).CompleteAsync(Chat, CancellationToken.None));

        Assert.Contains("not running", ex.Message);
    }

    [Fact]
    public async Task Ollama_says_so_when_the_answer_cannot_be_read()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json("this is not json") };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ollama(server).CompleteAsync(Chat, CancellationToken.None));

        Assert.Contains("could not be read", ex.Message);
    }

    [Fact]
    public async Task Ollama_streams_the_pieces_in_order_and_stops_at_done()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Lines(OllamaPiece("Hel"), OllamaPiece("lo"), OllamaPiece("", done: true), OllamaPiece("never shown")) };

        var pieces = await ReadAll(Ollama(server).StreamAsync(Chat, CancellationToken.None));

        Assert.Equal("Hello", string.Concat(pieces));
        using var doc = JsonDocument.Parse(server.Requests[0].Body);
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task Ollama_stream_skips_a_broken_line_and_hides_reasoning_text()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Lines(OllamaPiece("<think>hidden"), OllamaPiece(" thoughts</think>Vis"), "{broken json", OllamaPiece("ible", done: true)) };

        var pieces = await ReadAll(Ollama(server).StreamAsync(Chat, CancellationToken.None));

        Assert.Equal("Visible", string.Concat(pieces));
    }

    [Fact]
    public async Task Ollama_stream_turns_an_error_line_into_the_general_message()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Lines(OllamaPiece("Start"), "{\"error\":\"model crashed\"}") };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => ReadAll(Ollama(server).StreamAsync(Chat, CancellationToken.None)));

        Assert.Contains("returned an error", ex.Message);
    }

    [Fact]
    public async Task Ollama_stream_says_so_when_it_is_not_running()
    {
        var server = new FakeLlmServer { Respond = () => throw new HttpRequestException("connection refused") };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => ReadAll(Ollama(server).StreamAsync(Chat, CancellationToken.None)));

        Assert.Contains("not running", ex.Message);
    }

    // ================================================================ the hosted API (OpenAI format)

    [Fact]
    public async Task OpenAi_sends_the_key_as_a_bearer_token_and_the_conversation_to_chat_completions()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OpenAiAnswer("Hi there")) };

        var answer = await OpenAi(server).CompleteAsync(Chat, CancellationToken.None);

        Assert.Equal("Hi there", answer);
        var (url, body, auth) = Assert.Single(server.Requests);
        Assert.Equal("https://api.example.com/v1/chat/completions", url);
        Assert.Equal("Bearer secret-key", auth);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("gpt-test", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task OpenAi_asks_for_a_JSON_object_when_the_caller_wants_JSON()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OpenAiAnswer("{}")) };

        await OpenAi(server).CompleteAsync(Chat, CancellationToken.None, json: true);

        using var doc = JsonDocument.Parse(server.Requests[0].Body);
        Assert.Equal("json_object", doc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAi_without_a_key_does_not_call_the_server_at_all()
    {
        var server = new FakeLlmServer();

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(server, key: "").CompleteAsync(Chat, CancellationToken.None));
        await Assert.ThrowsAsync<AiUnavailableException>(() => ReadAll(OpenAi(server, key: " ").StreamAsync(Chat, CancellationToken.None)));

        Assert.Contains("no API key", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused our key")]
    [InlineData(HttpStatusCode.Forbidden, "refused our key")]
    [InlineData(HttpStatusCode.TooManyRequests, "busy or over its limit")]
    [InlineData(HttpStatusCode.InternalServerError, "returned an error")]
    public async Task OpenAi_explains_the_status_in_plain_words(HttpStatusCode status, string expected)
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json("{}", status) };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(server).CompleteAsync(Chat, CancellationToken.None));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task OpenAi_says_so_when_the_service_cannot_be_reached()
    {
        var server = new FakeLlmServer { Respond = () => throw new HttpRequestException("no route") };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(server).CompleteAsync(Chat, CancellationToken.None));

        Assert.Contains("Could not reach", ex.Message);
    }

    [Fact]
    public async Task OpenAi_reasoning_text_is_removed_from_the_answer()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json(OpenAiAnswer("<think>hmm</think>Final.")) };

        Assert.Equal("Final.", await OpenAi(server).CompleteAsync(Chat, CancellationToken.None));
    }

    [Fact]
    public async Task OpenAi_stream_reads_the_data_lines_and_stops_at_DONE()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Lines(": keep-alive comment", OpenAiPiece("Hel"), "", OpenAiPiece("lo"), "data: [DONE]", OpenAiPiece("never shown")) };

        var pieces = await ReadAll(OpenAi(server).StreamAsync(Chat, CancellationToken.None));

        Assert.Equal("Hello", string.Concat(pieces));
        using var doc = JsonDocument.Parse(server.Requests[0].Body);
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task OpenAi_stream_with_an_error_status_gives_the_plain_message()
    {
        var server = new FakeLlmServer { Respond = () => FakeLlmServer.Json("{}", HttpStatusCode.TooManyRequests) };

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => ReadAll(OpenAi(server).StreamAsync(Chat, CancellationToken.None)));

        Assert.Contains("busy or over its limit", ex.Message);
    }
}
