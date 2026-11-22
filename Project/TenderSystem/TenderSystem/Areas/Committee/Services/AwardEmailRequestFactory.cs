using System.Globalization;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Services.Ai;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (TOP-02) - turns the prepared award payloads into the requests of Wadea's AiAwardEmailService.
//
// Pure functions: no database, no AI call, no email, no logging. Everything comes from the payloads that
// AwardEmailPreparationService built from the saved award facts.
//
// VerifiedTerms: the AI service calls them "verified" because the email must repeat them exactly. Here they are
// only what the winner itself put in its OWN offer (delivery, validity, payment terms, guarantee). Every line says
// "stated in your offer" / "submitted with your offer", so the email never claims that anyone approved them.
//
// ConfidentialTerms (names the AI answer must never contain):
//   - winner email:       the names of the OTHER suppliers that took part.
//   - unsuccessful email: the winner's name, the other suppliers' names and the winning price.
//   The recipient's own name is never in its list (the email greets the supplier by that name).
// The AI check ignores terms shorter than 3 characters and does a plain "contains" match, so a very short or
// overlapping name can make the AI answer be refused; the caller then simply uses the fallback template.
public static class AwardEmailRequestFactory
{
    /// <summary>Signed at the end of every AI-written email.</summary>
    public const string BuyerName = "MS.WAND";

    // Free-text payment terms longer than this are left out (cutting them could change their meaning).
    private const int MaxPaymentTermsLength = 200;

    public static AwardEmailRequest ForWinner(WinningSupplierEmailPayload p, AwardEmailPreparationResult prepared) =>
        new(AwardEmailRequest.Winner,
            SupplierName(p.SupplierCompanyName, p.RecipientName),
            p.TenderNumber, p.TenderTitle, BuyerName,
            AwardedPrice: p.FinalTotalPrice,
            Currency: p.Currency,
            VerifiedTerms: VerifiedTerms(p),
            ConfidentialTerms: OtherSupplierNames(prepared, p.SupplierCompanyName));

    public static AwardEmailRequest ForUnsuccessful(UnsuccessfulSupplierEmailPayload p, AwardEmailPreparationResult prepared)
    {
        // Other suppliers' names include the winner's. The winning price is added too (the AI check also rejects
        // every number that is not in the facts, so this is a second safety net).
        var confidential = OtherSupplierNames(prepared, p.SupplierCompanyName);
        confidential.AddRange(prepared.WinningPriceTexts);

        return new(AwardEmailRequest.Unsuccessful,
            SupplierName(p.SupplierCompanyName, p.RecipientName),
            p.TenderNumber, p.TenderTitle, BuyerName,
            ConfidentialTerms: confidential);
    }

    // What the winner stated in its own offer. A missing or zero value is left out; nothing is guessed.
    public static List<string> VerifiedTerms(WinningSupplierEmailPayload p)
    {
        var terms = new List<string>();
        if (p.DeliveryDays > 0)
            terms.Add($"Delivery time stated in your offer: {p.DeliveryDays} days");
        if (p.ValidityDays > 0)
            terms.Add($"Offer validity stated in your offer: {p.ValidityDays} days");

        var payment = OneLine(p.PaymentTerms);
        if (payment.Length > 0 && payment.Length <= MaxPaymentTermsLength)
            terms.Add($"Payment terms stated in your offer: {payment}");

        var g = p.Guarantee;
        if (g is not null && g.Amount > 0 && g.ValidityDays > 0
            && !string.IsNullOrWhiteSpace(g.GuaranteeType) && !string.IsNullOrWhiteSpace(g.Currency))
        {
            terms.Add($"Guarantee submitted with your offer: {OneLine(g.GuaranteeType)} of "
                + $"{g.Amount.ToString("N2", CultureInfo.InvariantCulture)} {OneLine(g.Currency)}, valid for {g.ValidityDays} days");
        }
        return terms;
    }

    // Every participant's name except the recipient's own.
    private static List<string> OtherSupplierNames(AwardEmailPreparationResult prepared, string recipientCompanyName) =>
        prepared.ParticipantNames
            .Where(name => !string.Equals(name, recipientCompanyName?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string SupplierName(string companyName, string recipientName) =>
        !string.IsNullOrWhiteSpace(companyName) ? companyName.Trim() : recipientName;

    // One line of text: line breaks, tabs and other control characters become spaces.
    private static string OneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var chars = text.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
