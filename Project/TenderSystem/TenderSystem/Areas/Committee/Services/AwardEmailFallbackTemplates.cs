using System.Globalization;
using System.Text;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Services.Email;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (TOP-02, approved ownership of the whole voting feature).
//
// Deterministic, professional English texts for the Awarded supplier emails. These are the FALLBACK used
// when the AI is off, fails, or returns text that does not pass validation (see AwardEmailPayloads.cs).
// Pure functions: no database, no email sending, no AI, no logging.
//
// - Only the fields of the given payload are used, so the unsuccessful text cannot contain winner data.
// - Subject and body lines are cleaned of line breaks, so a title can never inject extra lines or headers.
// - HtmlBody is built by the shared EmailTemplates.Announcement, which HTML-encodes every line.
// - Nothing is invented: a value that is missing or zero is left out, and guarantee lines appear only when a
//   complete structured AwardGuaranteeDetails exists.
public static class AwardEmailFallbackTemplates
{
    private const string Sender = "the Sourcing Team";

    public static AwardEmailContent Winner(WinningSupplierEmailPayload p)
    {
        var tender = TenderLabel(p.TenderNumber, p.TenderTitle);
        var subject = Clean($"Award confirmation - Tender {p.TenderNumber}");

        var lines = new List<string>
        {
            $"Dear {NameOrDefault(p.RecipientName)},",
            "",
            $"We are pleased to confirm that your offer for {tender} has been selected and the tender has been awarded to {NameOrDefault(p.SupplierCompanyName)}.",
            "",
            "Award details:",
            string.IsNullOrWhiteSpace(p.TenderTitle)
                ? $"- Tender: {Clean(p.TenderNumber)}"
                : $"- Tender: {Clean(p.TenderNumber)} - {Clean(p.TenderTitle)}",
            $"- Awarded price: {Money(p.FinalTotalPrice, p.Currency)}"
        };
        if (p.DeliveryDays > 0) lines.Add($"- Delivery time: {p.DeliveryDays} days");
        if (p.ValidityDays > 0) lines.Add($"- Offer validity: {p.ValidityDays} days");
        if (!string.IsNullOrWhiteSpace(p.PaymentTerms)) lines.Add($"- Payment terms: {Clean(p.PaymentTerms)}");

        if (HasCompleteGuarantee(p.Guarantee))
        {
            var g = p.Guarantee!;
            lines.Add("");
            lines.Add("Guarantee requirement:");
            lines.Add($"- Type: {Clean(g.GuaranteeType)}");
            lines.Add($"- Amount: {Money(g.Amount, g.Currency)}");
            lines.Add($"- Validity: {g.ValidityDays} days");
        }

        lines.Add("");
        lines.Add("Next steps: our sourcing team will contact you shortly about the next steps. Any further requirements, such as guarantees, will be communicated to you directly by the sourcing team.");
        lines.Add("");
        lines.Add("Thank you for your participation and for the effort you put into your offer.");
        lines.Add("");
        lines.Add("Kind regards,");
        lines.Add("Sourcing Team");

        return Build(subject, lines);
    }

    public static AwardEmailContent Unsuccessful(UnsuccessfulSupplierEmailPayload p)
    {
        var tender = TenderLabel(p.TenderNumber, p.TenderTitle);
        var subject = Clean($"Outcome of your offer - Tender {p.TenderNumber}");

        var lines = new List<string>
        {
            $"Dear {NameOrDefault(p.RecipientName)},",
            "",
            $"Thank you for submitting an offer for {tender}.",
            "",
            "After careful consideration, we regret to inform you that your offer was not selected this time.",
            "",
            "We appreciate the time and effort you put into your offer and hope to work with you on future tenders.",
            "",
            "Kind regards,",
            "Sourcing Team"
        };

        return Build(subject, lines);
    }

    private static AwardEmailContent Build(string subject, List<string> lines)
    {
        var text = string.Join("\n", lines);
        return new AwardEmailContent
        {
            Subject = subject,
            TextBody = text,
            // Announcement encodes every line, so nothing from the payload can inject HTML.
            HtmlBody = EmailTemplates.Announcement(subject, text, Sender)
        };
    }

    private static bool HasCompleteGuarantee(AwardGuaranteeDetails? g) =>
        g is not null
        && !string.IsNullOrWhiteSpace(g.GuaranteeType)
        && g.Amount > 0
        && !string.IsNullOrWhiteSpace(g.Currency)
        && g.ValidityDays > 0;

    private static string TenderLabel(string number, string title) =>
        string.IsNullOrWhiteSpace(title) ? $"tender {Clean(number)}" : $"tender {Clean(number)} ({Clean(title)})";

    private static string Money(decimal amount, string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? amount.ToString("N2", CultureInfo.InvariantCulture)
            : $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {Clean(currency)}";

    private static string NameOrDefault(string? name) => string.IsNullOrWhiteSpace(name) ? "Supplier" : Clean(name);

    // Single-line text: control characters and line breaks become spaces.
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(char.IsControl(c) ? ' ' : c);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
