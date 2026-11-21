using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TenderSystem.Services.Ai;

/// <summary>
/// The verified facts for ONE award email (AI-15). Whoever calls the service fills this from the database, never from user text.
/// Winner: price, currency and the verified terms are required. Unsuccessful: leave the three empty, and list in
/// <see cref="ConfidentialTerms"/> everything that must NOT appear (the winner's name, the winning price, other suppliers' names...).
/// </summary>
public sealed record AwardEmailRequest(
    string Kind,
    string SupplierName,
    string TenderNumber,
    string TenderTitle,
    string BuyerName,
    decimal? AwardedPrice = null,
    string? Currency = null,
    IReadOnlyList<string>? VerifiedTerms = null,
    IReadOnlyList<string>? ConfidentialTerms = null)
{
    public const string Winner = "Winner";
    public const string Unsuccessful = "Unsuccessful";
}

/// <summary>Ok = true: Subject and Body are safe to use (plain text, the caller puts it in its email template). Ok = false: use the fallback template.</summary>
public sealed record AwardEmailDraft(bool Ok, string Subject, string Body, string Reason, string Model);

/// <summary>
/// Writes the award email in English from the verified facts only, then CHECKS it with code before anyone can use it:
/// no number that is not in the facts, no links or addresses, no placeholders, nothing from the confidential list, and for the winner
/// the price, currency and every verified term. Any failure gives Ok = false so the caller sends its fallback template.
/// It sends nothing and stores nothing.
/// </summary>
public sealed class AiAwardEmailService
{
    private static readonly Regex Placeholder = new(@"\[[^\]]*\]|\{\{|\}\}|<[^>]*>|\bXXX+\b|lorem ipsum", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LinkOrAddress = new(@"https?://|www\.|@", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MoneyWord = new(@"\b(JOD|USD|EUR|GBP|SAR|AED|EGP|KWD|QAR|dinars?|dollars?|euros?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CompetitiveWord = new(@"\b(rank(ed|ing)?|scor(e|es|ed|ing)|competitors?|winning (price|bid|offer|supplier)|awarded to|lowest|highest|cheaper|more expensive)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string WinnerInstructions = """
        You write a short, professional email in English from a buyer to the supplier whose offer was SELECTED for a tender.
        Say that the offer was selected. State the awarded price with its currency exactly as given. List every verified term exactly as given, one per line starting with "- ".
        Say that the buyer will contact the supplier about the next steps. Do not give dates, deadlines, links, email addresses or any promise that is not in the data.
        Start with "Dear <supplier name>," and finish with the buyer's name. Plain text only, no markdown, no placeholders.
        Answer as JSON: {"subject": "one short line", "body": "the email text"}
        """;

    private const string UnsuccessfulInstructions = """
        You write a short, polite, professional email in English from a buyer to a supplier whose offer was NOT selected for a tender.
        Thank the supplier for taking part and say that, after the evaluation, their offer was not selected this time. Invite them to take part in future tenders.
        Do NOT mention any price, amount, score, ranking, reason, the selected supplier or any other supplier. Do not give dates, links, email addresses or promises.
        Start with "Dear <supplier name>," and finish with the buyer's name. Plain text only, no markdown, no placeholders.
        Answer as JSON: {"subject": "one short line", "body": "the email text"}
        """;

    private readonly AiGenerationService _ai;

    public AiAwardEmailService(AiGenerationService ai) => _ai = ai;

    /// <summary>true when the AI can write emails (its own AI_EMAIL_* model, or the shared AI).</summary>
    public bool Enabled => _ai.Enabled(AiGenerationService.Email);

    public async Task<AwardEmailDraft> DraftAsync(AwardEmailRequest request, CancellationToken ct = default)
    {
        var model = _ai.ModelName(AiGenerationService.Email);
        var problem = CheckRequest(request);
        if (problem is not null) return new AwardEmailDraft(false, "", "", problem, model);

        var facts = BuildFacts(request);
        var instructions = request.Kind == AwardEmailRequest.Winner ? WinnerInstructions : UnsuccessfulInstructions;

        string subject = "", body = "";
        var result = await _ai.GenerateAsync(new AiGenerationRequest(AiGenerationService.Email, "award-email-" + request.Kind.ToLowerInvariant(), instructions, facts,
            ExpectJson: true,
            Validate: json =>
            {
                var error = Check(request, facts, json, out var s, out var b);
                if (error is null) { subject = s; body = b; }
                return error;
            }), ct);

        return result.Ok
            ? new AwardEmailDraft(true, subject, body, "", result.Model)
            : new AwardEmailDraft(false, "", "", result.Reason, result.Model);
    }

    // ------------------------------------------------------------------ the facts

    private static string? CheckRequest(AwardEmailRequest r)
    {
        if (r.Kind is not (AwardEmailRequest.Winner or AwardEmailRequest.Unsuccessful)) return "Kind must be Winner or Unsuccessful.";
        if (string.IsNullOrWhiteSpace(r.SupplierName) || string.IsNullOrWhiteSpace(r.TenderNumber) || string.IsNullOrWhiteSpace(r.TenderTitle) || string.IsNullOrWhiteSpace(r.BuyerName))
            return "Supplier name, tender number, tender title and buyer name are required.";
        if (r.Kind == AwardEmailRequest.Winner && (r.AwardedPrice is null or <= 0 || string.IsNullOrWhiteSpace(r.Currency)))
            return "The winner email needs the awarded price and the currency.";
        if (r.Kind == AwardEmailRequest.Unsuccessful && (r.AwardedPrice is not null || r.VerifiedTerms is { Count: > 0 }))
            return "An unsuccessful email must not be given a price or terms.";
        return null;
    }

    private static string BuildFacts(AwardEmailRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Email type: {(r.Kind == AwardEmailRequest.Winner ? "offer selected" : "offer not selected")}");
        sb.AppendLine($"Supplier name: {r.SupplierName.Trim()}");
        sb.AppendLine($"Tender number: {r.TenderNumber.Trim()}");
        sb.AppendLine($"Tender title: {r.TenderTitle.Trim()}");
        sb.AppendLine($"Buyer name: {r.BuyerName.Trim()}");
        if (r.Kind == AwardEmailRequest.Winner)
        {
            sb.AppendLine($"Awarded price: {r.AwardedPrice!.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {r.Currency!.Trim()}");
            sb.AppendLine("Verified terms:");
            if (r.VerifiedTerms is { Count: > 0 }) foreach (var t in r.VerifiedTerms) sb.AppendLine("- " + t.Trim());
            else sb.AppendLine("- none");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ the checks (code, not the model)

    private static string? Check(AwardEmailRequest r, string facts, string json, out string subject, out string body)
    {
        subject = body = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            subject = (doc.RootElement.TryGetProperty("subject", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null)?.Trim() ?? "";
            body = (doc.RootElement.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null)?.Replace("\r", "").Trim() ?? "";
        }
        catch (JsonException)
        {
            return "The answer was not valid JSON.";
        }

        if (subject.Length is < 5 or > 150 || subject.Contains('\n')) return "The subject must be one line of 5 to 150 characters.";
        if (body.Length is < 60 or > 1800) return "The body must be 60 to 1800 characters.";

        var text = subject + "\n" + body;
        if (Placeholder.IsMatch(text)) return "It contains a placeholder or markup.";
        if (LinkOrAddress.IsMatch(text)) return "It contains a link or an email address that is not in the data.";
        if (!body.Contains(r.SupplierName.Trim(), StringComparison.OrdinalIgnoreCase)) return "The body must greet the supplier by the name in the data.";
        if (!text.Contains(r.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase)) return "It must mention the tender number from the data.";

        if (AiFactCheck.MentionsAny(text, r.ConfidentialTerms)) return "It mentions confidential information.";

        var unsupported = AiFactCheck.UnsupportedNumbers(text, facts);
        if (unsupported.Count > 0) return "It contains numbers that are not in the data: " + string.Join(", ", unsupported.Take(5)) + ".";

        if (r.Kind == AwardEmailRequest.Winner)
        {
            if (!AiFactCheck.ContainsAmount(body, r.AwardedPrice!.Value)) return "The body must state the awarded price exactly as in the data.";
            if (!body.Contains(r.Currency!.Trim(), StringComparison.OrdinalIgnoreCase)) return "The body must state the currency.";
            foreach (var term in r.VerifiedTerms ?? [])
            {
                var termNumbers = AiFactCheck.Numbers(term);
                var bodyNumbers = AiFactCheck.Numbers(body);
                if (termNumbers.Any(n => !bodyNumbers.Contains(n))) return "The body must include every verified term.";
                if (termNumbers.Count == 0 && !body.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)) return "The body must include every verified term exactly as given.";
            }
        }
        else
        {
            if (MoneyWord.IsMatch(text)) return "An unsuccessful email must not mention money.";
            if (CompetitiveWord.IsMatch(text)) return "An unsuccessful email must not mention scores, rankings or other suppliers.";
        }
        return null;
    }
}
