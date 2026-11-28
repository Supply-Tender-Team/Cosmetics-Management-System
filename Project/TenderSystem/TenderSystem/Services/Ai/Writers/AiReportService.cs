using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TenderSystem.Services.Ai;

/// <summary>A label and a value that the SYSTEM calculated (a count, a rate, an amount with its currency).</summary>
public sealed record AiReportFigure(string Label, string Value);

/// <summary>
/// What the caller gives the service (AI-16). Everything here is verified data from the database, never text typed by a user.
/// </summary>
/// <param name="Title">Server-owned report title, for example "Supplier offers".</param>
/// <param name="Scope">Server-owned scope in words, for example "All suppliers, 1 Oct 2026 to 31 Oct 2026".</param>
/// <param name="RecordCount">How many records the report really covers (all of them, not only the visible page).</param>
/// <param name="KeyFigures">The numbers the system calculated. The model may only repeat them.</param>
/// <param name="FactsText">The verified data as plain text: full aggregates, plus a short list of supporting rows. At most <see cref="AiReportService.MaxFactsChars"/> characters.</param>
/// <param name="Limits">Honest coverage notes, for example "Only the 40 newest rows are listed; the totals cover all 1,350." They are returned unchanged.</param>
public sealed record AiReportFacts(string Title, string Scope, int RecordCount, IReadOnlyList<AiReportFigure> KeyFigures, string FactsText, IReadOnlyList<string>? Limits = null);

/// <summary>The written part of a report. Summary, findings, attention points and details come from the model after code checks; CoverageNotes come from the caller.</summary>
public sealed record AiReportNarrative(string Summary, IReadOnlyList<string> Findings, IReadOnlyList<string> NeedsAttention, IReadOnlyList<AiReportFigure> Details, IReadOnlyList<string> CoverageNotes);

/// <summary>
/// Status is one of: ok, disabled, no-data, invalid-request, too-large, timeout, unavailable (the model could not answer), rejected (the answer failed the checks twice).
/// Ok = true only with status "ok". Message is safe to show to the Admin.
/// </summary>
public sealed record AiReportResult(bool Ok, string Status, string Message, AiReportNarrative? Narrative, string Model);

/// <summary>How much to write. Summary: a short overview. Analysis: the overview plus more findings, review points and details.</summary>
public enum AiReportDepth { Summary, Analysis }

/// <summary>AI_REPORT_TIMEOUT_SECONDS: time allowed for ONE attempt (default 90). A rejected answer is retried once, so the longest wait is twice this.</summary>
public sealed class AiReportOptions
{
    public int TimeoutSeconds { get; set; } = 90;
}

/// <summary>
/// AI-16: writes the summary and the analysis of a report from verified facts. Inject it like any other service:
///
///   var result = await _report.SummarizeAsync(new AiReportFacts(title, scope, count, figures, factsText, limits), ct);
///   if (result.Ok) { /* show result.Narrative */ } else { /* show the figures without AI text; result.Message says why */ }
///
/// The caller owns the data and the permissions: it must load the facts for the signed-in user and send only what that user may see.
/// This service owns the words: it asks the model (profile "insights", AI_INSIGHTS_* in .env), then CHECKS the answer with code before
/// returning it. Every number in the text must exist in the facts, there must be no link or placeholder, and the text must not pick a winner.
/// It never calls the database, never changes anything, and never throws for a model problem. The detailed contract is in AI-REPORT-CONTRACT.md.
/// </summary>
public sealed class AiReportService
{
    public const int MaxFactsChars = 12000;
    public const int MaxFigures = 20;

    private static readonly Regex Placeholder = new(@"\[[^\]]*\]|\{\{|\}\}|<[^>]*>|\bTODO\b|\bTBD\b|\bXXX+\b|lorem ipsum", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LinkOrAddress = new(@"https?://|www\.|@", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Decision = new(@"\b(award(ed)? (the )?(contract|tender) to|should (award|select|choose)|we recommend (awarding|selecting)|approve (this|the) (tender|award|supplier))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string Rules = """
        You are an assistant for the Admin of the sourcing and tender system of a cosmetics company. You read the verified facts of ONE report and write a short explanation for the Admin.
        You only explain and flag points for people to review. You never award, approve, reject or choose a supplier, and you never say what a person must decide.

        Rules:
        - Every number is already calculated by the system. Copy numbers exactly as written in the data. Never calculate a new total, percentage, average or difference.
        - Never add amounts of different currencies and never convert a currency.
        - If something is not in the data, say it is not available. Never write 0 for something that is missing.
        - If the data lists coverage limits, do not claim that everything was analysed.
        - Do not use digits except numbers that appear in the data. Do not number your points.
        - Plain English. No markdown, no HTML, no links, no email addresses, no placeholders.
        """;

    private const string SummaryShape = """
        Write: "summary" (at most 4 short sentences), "findings" (at most 3 short sentences about the clearest patterns), "needs_attention" (at most 3 short points that a person should review; empty list if none), "details" (an empty list).
        Reply with exactly this JSON shape: {"summary": string, "findings": [string], "needs_attention": [string], "details": []}
        """;

    private const string AnalysisShape = """
        Write: "summary" (at most 5 short sentences), "findings" (at most 6 short sentences about patterns, comparisons and anything unusual in the data), "needs_attention" (at most 6 short points that a person should review; empty list if none), "details" (at most 8 entries, each a short "label" and a "value" that repeats data, for example the largest group or the longest time).
        Reply with exactly this JSON shape: {"summary": string, "findings": [string], "needs_attention": [string], "details": [{"label": string, "value": string}]}
        """;

    private readonly AiGenerationService _ai;
    private readonly AiReportOptions _options;

    public AiReportService(AiGenerationService ai, IOptions<AiReportOptions> options)
    {
        _ai = ai;
        _options = options.Value;
    }

    /// <summary>true when a model is available for reports (AI_INSIGHTS_* or the shared AI settings).</summary>
    public bool Enabled => _ai.Enabled(AiGenerationService.Insights);

    /// <summary>The name of the model that writes the text (for the audit log).</summary>
    public string ModelName => _ai.ModelName(AiGenerationService.Insights);

    public Task<AiReportResult> SummarizeAsync(AiReportFacts facts, CancellationToken ct = default) => RunAsync(facts, AiReportDepth.Summary, ct);

    public Task<AiReportResult> AnalyzeAsync(AiReportFacts facts, CancellationToken ct = default) => RunAsync(facts, AiReportDepth.Analysis, ct);

    public async Task<AiReportResult> RunAsync(AiReportFacts facts, AiReportDepth depth, CancellationToken ct = default)
    {
        var model = ModelName;
        var problem = CheckFacts(facts);
        if (problem is not null) return Fail("invalid-request", problem, model);
        if (facts.FactsText.Length > MaxFactsChars)
            return Fail("too-large", $"The facts are longer than {MaxFactsChars} characters. Send the full totals and fewer supporting rows.", model);
        if (facts.RecordCount == 0)
            return Fail("no-data", "There are no records in this scope, so there is nothing to summarise.", model);   // no model call: an empty report is reported honestly
        if (!Enabled)
            return Fail("disabled", "The AI is switched off or not configured for reports.", model);

        var factsText = BuildFactsText(facts);
        var limits = (facts.Limits ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        AiReportNarrative? narrative = null;

        var result = await _ai.GenerateAsync(new AiGenerationRequest(AiGenerationService.Insights,
            depth == AiReportDepth.Summary ? "report-summary" : "report-analysis",
            Rules + "\n\n" + (depth == AiReportDepth.Summary ? SummaryShape : AnalysisShape),
            factsText,
            ExpectJson: true,
            Validate: json =>
            {
                var error = Check(json, factsText, depth, limits, out var parsed);
                if (error is null) narrative = parsed;
                return error;
            },
            TimeoutSeconds: Math.Clamp(_options.TimeoutSeconds, 10, 600)), ct);

        if (result.Ok && narrative is not null)
            return new AiReportResult(true, "ok", "", narrative, result.Model);

        if (result.Reason.Contains("too long", StringComparison.OrdinalIgnoreCase))
            return Fail("timeout", "The AI took too long to answer. Please try again.", result.Model);
        if (result.Rejected)
            return Fail("rejected", "The AI text did not pass the checks, so it is not shown. Please try again.", result.Model);
        return Fail("unavailable", string.IsNullOrWhiteSpace(result.Reason) ? "The AI could not answer. Please try again." : result.Reason, result.Model);
    }

    // ------------------------------------------------------------------ input

    private static string? CheckFacts(AiReportFacts f)
    {
        if (f is null) return "The facts are missing.";
        if (string.IsNullOrWhiteSpace(f.Title) || f.Title.Length > 200) return "The title is required (at most 200 characters).";
        if (string.IsNullOrWhiteSpace(f.Scope) || f.Scope.Length > 400) return "The scope is required (at most 400 characters).";
        if (f.RecordCount < 0) return "The record count cannot be negative.";
        if ((f.KeyFigures?.Count ?? 0) > MaxFigures) return $"At most {MaxFigures} key figures.";
        if (f.KeyFigures is not null && f.KeyFigures.Any(k => string.IsNullOrWhiteSpace(k.Label) || k.Label.Length > 80 || (k.Value ?? "").Length > 120))
            return "Every key figure needs a label (at most 80 characters) and a value (at most 120).";
        if (f.RecordCount > 0 && string.IsNullOrWhiteSpace(f.FactsText)) return "The facts text is required when the scope has records.";
        return null;
    }

    private static string BuildFactsText(AiReportFacts f)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Report: " + f.Title.Trim());
        sb.AppendLine("Scope: " + f.Scope.Trim());
        sb.AppendLine("Records in scope: " + f.RecordCount);
        if (f.KeyFigures is { Count: > 0 })
        {
            sb.AppendLine("Key figures (calculated by the system):");
            foreach (var k in f.KeyFigures) sb.AppendLine($"- {k.Label.Trim()}: {k.Value.Trim()}");
        }
        var limits = (f.Limits ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (limits.Count > 0)
        {
            sb.AppendLine("Coverage limits:");
            foreach (var l in limits) sb.AppendLine("- " + l.Trim());
        }
        sb.AppendLine("Data:");
        sb.AppendLine(f.FactsText.Trim());
        return sb.ToString();
    }

    // ------------------------------------------------------------------ the checks (code, not the model)

    private static string? Check(string json, string facts, AiReportDepth depth, List<string> limits, out AiReportNarrative? narrative)
    {
        narrative = null;
        string summary;
        var findings = new List<string>();
        var attention = new List<string>();
        var details = new List<AiReportFigure>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            summary = Text(root, "summary");
            findings = Strings(root, "findings");
            attention = Strings(root, "needs_attention");
            if (root.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Array)
                foreach (var item in d.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && Text(item, "label") is { Length: > 0 } label && Text(item, "value") is { Length: > 0 } value)
                        details.Add(new AiReportFigure(label, value));
        }
        catch (JsonException)
        {
            return "The answer was not valid JSON.";
        }

        if (summary.Length is < 30 or > 1200) return "The summary must be 30 to 1200 characters.";

        var maxFindings = depth == AiReportDepth.Summary ? 3 : 6;
        var maxDetails = depth == AiReportDepth.Summary ? 0 : 8;
        findings = findings.Take(maxFindings).ToList();
        attention = attention.Take(maxFindings).ToList();
        details = details.Take(maxDetails).ToList();

        var all = string.Join("\n", new[] { summary }.Concat(findings).Concat(attention).Concat(details.Select(x => x.Label + " " + x.Value)));
        if (Placeholder.IsMatch(all)) return "It contains a placeholder or markup.";
        if (LinkOrAddress.IsMatch(all)) return "It contains a link or an email address that is not in the data.";
        if (Decision.IsMatch(all)) return "It tells people to award or select a supplier. It may only explain and flag points to review.";

        var unsupported = AiFactCheck.UnsupportedNumbers(all, facts);
        if (unsupported.Count > 0) return "It contains numbers that are not in the data: " + string.Join(", ", unsupported.Take(5)) + ". Copy numbers exactly and never calculate new ones.";

        narrative = new AiReportNarrative(summary, findings, attention, details, limits);
        return null;
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static List<string> Strings(JsonElement e, string name)
    {
        var list = new List<string>();
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var item in v.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && (item.GetString() ?? "").Trim() is { Length: > 0 } s)
                    list.Add(s.Length > 400 ? s[..400].TrimEnd() + "…" : s);
        return list;
    }

    private static AiReportResult Fail(string status, string message, string model) => new(false, status, message, null, model);
}
