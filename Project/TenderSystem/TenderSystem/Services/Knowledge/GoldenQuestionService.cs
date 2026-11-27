using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Ai;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;

namespace TenderSystem.Services.Knowledge;

public sealed record GoldenQuestionItem(int Id, string Question, string Role, string? ExpectedDocument, string? MustContain,
    DateTime? LastRunAt, bool? LastPassed, string? LastDetail);

/// <summary>Result of one run of all test questions. Note says why answers were not checked, or null.</summary>
public sealed record GoldenRunSummary(int Total, int Passed, int Failed, int AnswersChecked, string? Note);

/// <summary>
/// The Admin's test questions for the assistant (AI-20). One run asks every question as its role, exactly like a user of that role:
///   - the SEARCH check: the expected document must be found (or nothing at all when no document is expected, which proves a role cannot reach a document);
///   - the ANSWER check (optional, slower): the model answers from the passages found and the answer must contain all the given words.
/// Run it after changing documents, roles, the embedding model or the chat model, to see at once what broke.
/// </summary>
public sealed class GoldenQuestionService
{
    public const int MaxQuestions = 40;
    private const int MaxAnswersPerRun = 15;

    private readonly ApplicationDbContext _db;
    private readonly KnowledgeBaseService _knowledge;
    private readonly QuestionService _chat;
    private readonly AuditService _audit;
    private readonly ILogger<GoldenQuestionService> _log;

    public GoldenQuestionService(ApplicationDbContext db, KnowledgeBaseService knowledge, QuestionService chat, AuditService audit, ILogger<GoldenQuestionService> log)
    {
        _db = db;
        _knowledge = knowledge;
        _chat = chat;
        _audit = audit;
        _log = log;
    }

    public async Task<List<GoldenQuestionItem>> ListAsync(CancellationToken ct) =>
        await _db.GoldenQuestions.AsNoTracking().OrderBy(q => q.Id)
            .Select(q => new GoldenQuestionItem(q.Id, q.Question, q.Role, q.ExpectedDocument, q.MustContain, q.LastRunAt, q.LastPassed, q.LastDetail))
            .ToListAsync(ct);

    public async Task<(bool Ok, string Message)> AddAsync(string userId, string? question, string? role, string? expectedDocument, string? mustContain, CancellationToken ct)
    {
        question = question?.Trim() ?? "";
        if (question.Length == 0 || question.Length > 500) return (false, "Type a question of up to 500 characters.");
        if (role is null || !KnowledgeBaseService.AllowedRoleNames.Contains(role)) return (false, "Choose the role the question is asked as.");
        if (await _db.GoldenQuestions.CountAsync(ct) >= MaxQuestions) return (false, $"At most {MaxQuestions} test questions are kept.");

        var terms = SplitTerms(mustContain);
        _db.GoldenQuestions.Add(new GoldenQuestion
        {
            Question = question,
            Role = role,
            ExpectedDocument = string.IsNullOrWhiteSpace(expectedDocument) ? null : Limit(expectedDocument.Trim(), 200),
            MustContain = terms.Count == 0 ? null : Limit(string.Join(", ", terms), 300),
            CreatedById = userId,
        });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("GoldenQuestionAdded", "GoldenQuestion", "new", $"role {role}");
        return (true, "The test question was added.");
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken ct)
    {
        var count = await _db.GoldenQuestions.Where(q => q.Id == id).ExecuteDeleteAsync(ct);
        if (count > 0) await _audit.LogAsync("GoldenQuestionRemoved", "GoldenQuestion", id.ToString(), "removed");
        return count > 0;
    }

    public async Task<GoldenRunSummary> RunAsync(bool withAnswers, CancellationToken ct)
    {
        var questions = await _db.GoldenQuestions.OrderBy(q => q.Id).ToListAsync(ct);
        var passed = 0;
        var answers = 0;
        string? note = null;
        var canAnswer = withAnswers && _chat.Enabled && _chat.CompanyDataAllowed;
        if (withAnswers && !canAnswer) note = "The answers were not checked: the assistant is off, or it does not use company documents with a hosted model.";

        foreach (var q in questions)
        {
            var (ok, detail) = await CheckSearchAsync(q, ct);
            var terms = SplitTerms(q.MustContain);
            if (ok && canAnswer && terms.Count > 0)
            {
                if (answers < MaxAnswersPerRun)
                {
                    answers++;
                    (ok, detail) = await CheckAnswerAsync(q, terms, detail, ct);
                }
                else note = $"Only the first {MaxAnswersPerRun} answers were checked in one run.";
            }

            q.LastRunAt = DateTime.UtcNow;
            q.LastPassed = ok;
            q.LastDetail = Limit(detail, 500);
            if (ok) passed++;
        }

        await _db.SaveChangesAsync(CancellationToken.None);
        await _audit.LogAsync("GoldenQuestionsRun", "GoldenQuestion", "all", $"{passed} of {questions.Count} passed, {answers} answer(s) checked");
        return new GoldenRunSummary(questions.Count, passed, questions.Count - passed, answers, note);
    }

    private async Task<(bool Ok, string Detail)> CheckSearchAsync(GoldenQuestion q, CancellationToken ct)
    {
        List<KnowledgeHit> hits;
        try { hits = await _knowledge.SearchAsync(q.Role, q.Question, ct); }
        catch (AiUnavailableException ex) { return (false, "The search could not run: " + ex.Message); }

        var titles = hits.Select(h => h.Title).Distinct().ToList();
        if (string.IsNullOrWhiteSpace(q.ExpectedDocument))
        {
            return titles.Count == 0
                ? (true, "Nothing was found, as expected.")
                : (false, "Nothing should be found for this role, but the search found: " + string.Join(", ", titles));
        }

        var best = hits.FirstOrDefault(h => string.Equals(h.Title, q.ExpectedDocument, StringComparison.OrdinalIgnoreCase));
        if (best != null) return (true, $"Found \"{best.Title}\" (score {best.Score:0.00}).");
        return titles.Count == 0
            ? (false, $"\"{q.ExpectedDocument}\" was not found: the search found nothing.")
            : (false, $"\"{q.ExpectedDocument}\" was not found. The search found: " + string.Join(", ", titles));
    }

    private async Task<(bool Ok, string Detail)> CheckAnswerAsync(GoldenQuestion q, List<string> terms, string searchDetail, CancellationToken ct)
    {
        try
        {
            var hits = await _knowledge.SearchAsync(q.Role, q.Question, ct);
            var (passages, _) = KnowledgeBaseService.NumberPassages(hits);
            var messages = new List<AiChatMessage>
            {
                new("system", AiChatPrompts.BuildSystemPrompt(q.Role, null, sources: passages)),
                new("user", q.Question),
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_chat.TimeoutSeconds));
            var answer = await _chat.Provider.CompleteAsync(messages, timeout.Token);

            var missing = terms.Where(t => !answer.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
            return missing.Count == 0
                ? (true, searchDetail + " The answer contains all the expected words.")
                : (false, searchDetail + " The answer is missing: " + string.Join(", ", missing));
        }
        catch (AiUnavailableException ex) { return (false, searchDetail + " The model did not answer: " + ex.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (false, searchDetail + " The model took too long to answer."); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "A test question could not be answered");
            return (false, searchDetail + " The answer could not be checked.");
        }
    }

    private static List<string> SplitTerms(string? text) =>
        (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => t.Length > 0).Take(10).ToList();

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];
}
