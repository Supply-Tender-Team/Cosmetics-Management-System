using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Ai;
using Xunit.Abstractions;

namespace TenderSystem.Tests.Ai;

/// <summary>A test that runs only when the environment variable AI_ROUTING_TEST=1 is set (it needs Ollama running).</summary>
public sealed class OptionalFactAttribute : FactAttribute
{
    public OptionalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AI_ROUTING_TEST") != "1")
            Skip = "Needs Ollama. Set AI_ROUTING_TEST=1 (and optionally OLLAMA_MODEL, OLLAMA_BASE_URL) to run it.";
    }
}

/// <summary>
/// The "routing score": for natural questions of every role, does the model choose the right lookup? It is the number to keep
/// (or improve) whenever the lookup code changes. Run:  $env:AI_ROUTING_TEST = "1"; dotnet test --filter RoutingScore
/// </summary>
public class RoutingScoreTests
{
    private readonly ITestOutputHelper _output;
    public RoutingScoreTests(ITestOutputHelper output) => _output = output;

    // role, question, the tool names that are a correct choice
    private static readonly (string Role, string Question, string[] Accept)[] Questions =
    [
        (Roles.Admin, "How many suppliers are pending approval?", ["counts", "suppliers"]),
        (Roles.Admin, "Who logged in today?", ["audit"]),
        (Roles.Admin, "Show me the newest users", ["users"]),
        (Roles.Admin, "Which suppliers are suspended?", ["suppliers"]),
        (Roles.Admin, "How do I reset a user's password?", ["none"]),
        (Roles.Admin, "Any new contact messages?", ["contact_messages"]),
        (Roles.Admin, "How many users are there per role?", ["counts", "users"]),

        (Roles.Requester, "Which of my requests were rejected?", ["requests"]),
        (Roles.Requester, "How many requests are still drafts?", ["counts", "requests"]),
        (Roles.Requester, "Do I have anything overdue?", ["requests", "deadlines"]),
        (Roles.Requester, "How do I create a request?", ["none"]),
        (Roles.Requester, "What is the status of DEMO-PR-001?", ["classic", "requests"]),

        (Roles.SourcingOfficer, "Which tenders have no offers yet?", ["tenders"]),
        (Roles.SourcingOfficer, "Who has the lowest price on TND-002?", ["offers"]),
        (Roles.SourcingOfficer, "What closes in the next 3 days?", ["tenders", "deadlines"]),
        (Roles.SourcingOfficer, "Check the compliance of TND-001", ["classic"]),
        (Roles.SourcingOfficer, "Which requests are waiting for me?", ["requests"]),

        (Roles.EvaluationCommittee, "How far am I with my scoring?", ["evaluation_progress"]),
        (Roles.EvaluationCommittee, "Which tenders am I evaluating?", ["tenders"]),
        (Roles.EvaluationCommittee, "Compare the offers on TND-002", ["classic", "offers"]),
        (Roles.EvaluationCommittee, "What is due soon?", ["deadlines"]),

        (Roles.SourcingManager, "What is waiting for my review?", ["tenders"]),
        (Roles.SourcingManager, "How many tenders are awarded?", ["counts", "tenders"]),
        (Roles.SourcingManager, "Summarize the report of TND-005", ["classic"]),

        (Roles.TopCommittee, "What do I have to decide on?", ["tenders", "votes"]),
        (Roles.TopCommittee, "How many votes were cast?", ["votes"]),
        (Roles.TopCommittee, "Tell me about TND-007", ["classic"]),

        (Roles.Supplier, "Which invitations have I not answered?", ["invitations"]),
        (Roles.Supplier, "When is my next deadline?", ["deadlines"]),
        (Roles.Supplier, "How many offers did I submit?", ["counts", "offers"]),
        (Roles.Supplier, "What is the status of my offer on TND-001?", ["offers"]),
        (Roles.Supplier, "What are the terms of TND-001?", ["classic", "tenders"]),

        (Roles.Requester, "Do I have unread notifications?", ["notifications"]),
        (Roles.Supplier, "Show my latest notifications", ["notifications"]),
        (Roles.SourcingOfficer, "What notifications did I get today?", ["notifications"]),
    ];

    [OptionalFact]
    public async Task Report_the_routing_score()
    {
        var ollama = new OllamaOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434",
            Model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "qwen3.5:9b",
        };
        var provider = new OllamaChatProvider(new PlainHttpClientFactory(), Options.Create(ollama));
        var planner = new AiLookupPlanner([provider], Options.Create(new AiChatOptions { Provider = "ollama" }),
            Options.Create(new AiPlannerOptions { TimeoutSeconds = 60 }), NullLogger<AiLookupPlanner>.Instance);

        var correct = 0;
        var report = new StringBuilder();
        foreach (var (role, question, accept) in Questions)
        {
            var call = await planner.PlanAsync(role, false, question, [], null, null, CancellationToken.None);
            var chosen = call?.Tool ?? "(no choice)";
            var ok = accept.Contains(chosen);
            if (ok) correct++;
            report.AppendLine($"{(ok ? "ok  " : "MISS")} {role,-20} {question,-48} -> {chosen}   (wanted: {string.Join(" or ", accept)})");
        }

        _output.WriteLine(report.ToString());
        _output.WriteLine($"ROUTING SCORE: {correct} of {Questions.Length}");
        Assert.True(correct >= Questions.Length * 0.8, $"Routing score {correct} of {Questions.Length} is below 80%.");
    }

    private sealed class PlainHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(3) };
    }
}
