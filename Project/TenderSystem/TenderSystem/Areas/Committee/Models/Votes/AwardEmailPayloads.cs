namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (TOP-02, approved ownership of the whole voting feature).
//
// Data contracts for the future automatic AWARDED supplier emails. This file is data only:
// nothing here sends an email, calls the AI, writes to the database or creates a notification.
//
// CONFIDENTIALITY: each payload holds ONLY what that one recipient may see.
//   - WinningSupplierEmailPayload      -> the winner's own offer (price, terms) and nothing about other suppliers.
//   - UnsuccessfulSupplierEmailPayload -> tender number/title and the recipient's own name only. It has no
//     price, no winner name, no other offer, no committee votes and no evaluation data, by design:
//     the type simply has no property that could carry them.
//
// ---------------------------------------------------------------------------------------------------------
// PROPOSED AI CONTRACT FOR WADEA (not implemented; the Committee code must never call an AI provider itself)
// ---------------------------------------------------------------------------------------------------------
// One shared service in Wadea's AI area, for example:
//
//     Task<AwardEmailContent> DraftAwardEmailAsync(AwardEmailDraftRequest request, CancellationToken ct);
//
// Input  (AwardEmailDraftRequest):
//     Category  : "WinningSupplier" or "UnsuccessfulSupplier"   (AwardEmailCategory)
//     Language  : "English"
//     Tone      : "professional, formal, courteous"
//     Facts     : the recipient-safe payload above, as plain key/value text (and nothing else)
// Output (AwardEmailContent):
//     Subject   : one line of plain text
//     Body      : plain text (the caller encodes it for HTML)
//
// Rules the service must follow:
//   1. Use ONLY the supplied facts. Never invent or round prices, currencies, dates, guarantee amounts or
//      durations, legal terms or promises. A missing fact is left out, never guessed.
//   2. Validate the AI answer before returning it: every number and currency code in the text must appear in
//      the supplied facts, the tender number must be present, and no other supplier name or price may appear.
//   3. Never put another supplier's name, price, documents or any committee vote/evaluation detail in a prompt.
//      The UnsuccessfulSupplier request carries no winner data at all.
//   4. If the AI is disabled, times out, fails, or its answer fails validation, return the deterministic text
//      of AwardEmailFallbackTemplates for the same payload. The caller never sees an AI error.
//   5. Never throw into the voting flow. Run it AFTER the decision is committed and OUTSIDE the fourth voter's
//      request (a background job or an outbox row), never while the tender row is locked.
// ---------------------------------------------------------------------------------------------------------

/// <summary>The two recipient categories of an Awarded tender email.</summary>
public static class AwardEmailCategory
{
    public const string WinningSupplier = "WinningSupplier";
    public const string UnsuccessfulSupplier = "UnsuccessfulSupplier";
}

/// <summary>
/// The bank guarantee the supplier submitted with the winning offer (BidGuarantees table). It is filled by
/// AwardFactsService only when exactly one bank guarantee exists and its data is complete and valid.
/// The data is entered by the supplier and is NOT officially verified, so no text may say it is.
/// It holds only these four fields on purpose: no guarantor identity, reference number, issuer or verification code.
/// The templates print it only when every field is present and valid. Never fill it from the free-text tender terms.
/// </summary>
public sealed class AwardGuaranteeDetails
{
    public string GuaranteeType { get; init; } = "";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public int ValidityDays { get; init; }
}

/// <summary>Everything the winning supplier may be told, taken from the saved award facts.</summary>
public sealed class WinningSupplierEmailPayload
{
    public int TenderId { get; init; }
    public string TenderNumber { get; init; } = "";
    public string TenderTitle { get; init; } = "";

    public int SupplierProfileId { get; init; }
    public string SupplierCompanyName { get; init; } = "";
    /// <summary>Contact person, or the company name when no contact person is saved.</summary>
    public string RecipientName { get; init; } = "";
    public string RecipientEmail { get; init; } = "";

    /// <summary>The stored total of the winning bid exactly as saved.</summary>
    public decimal FinalTotalPrice { get; init; }
    public string Currency { get; init; } = "";
    public int DeliveryDays { get; init; }
    public int ValidityDays { get; init; }
    public string PaymentTerms { get; init; } = "";

    /// <summary>The supplier-submitted bank guarantee of this offer, or null when it is missing, incomplete or ambiguous.</summary>
    public AwardGuaranteeDetails? Guarantee { get; init; }
}

/// <summary>Everything an unsuccessful supplier may be told. Deliberately has no price or winner field.</summary>
public sealed class UnsuccessfulSupplierEmailPayload
{
    public int TenderId { get; init; }
    public string TenderNumber { get; init; } = "";
    public string TenderTitle { get; init; } = "";

    public int SupplierProfileId { get; init; }
    public string SupplierCompanyName { get; init; } = "";
    public string RecipientName { get; init; } = "";
    public string RecipientEmail { get; init; } = "";
}

/// <summary>The subject and text of one prepared email (before HTML encoding).</summary>
public sealed class AwardEmailContent
{
    public string Subject { get; init; } = "";
    /// <summary>Plain text with line breaks.</summary>
    public string TextBody { get; init; } = "";
    /// <summary>The same message as safely encoded HTML.</summary>
    public string HtmlBody { get; init; } = "";
}

/// <summary>The answer of AwardEmailPreparationService: prepared payloads, or a safe failure message.</summary>
public sealed class AwardEmailPreparationResult
{
    public bool Success { get; private init; }
    public string Message { get; private init; } = "";

    /// <summary>Null when the winner has no usable email address (see WinnerMissingEmail).</summary>
    public WinningSupplierEmailPayload? Winner { get; private init; }
    public bool WinnerMissingEmail { get; private init; }

    public IReadOnlyList<UnsuccessfulSupplierEmailPayload> Unsuccessful { get; private init; } = [];
    /// <summary>Unsuccessful suppliers skipped because they have no usable email address.</summary>
    public int UnsuccessfulMissingEmail { get; private init; }

    // ---- Only for the "must not mention" lists of the AI check (AwardEmailRequestFactory). Never printed in an email.
    /// <summary>Company names of every supplier that took part, the winner included.</summary>
    public IReadOnlyList<string> ParticipantNames { get; private init; } = [];
    /// <summary>The winning price written the usual ways (for example 1234.50 and 1,234.50).</summary>
    public IReadOnlyList<string> WinningPriceTexts { get; private init; } = [];

    public static AwardEmailPreparationResult Ok(
        WinningSupplierEmailPayload? winner, bool winnerMissingEmail,
        IReadOnlyList<UnsuccessfulSupplierEmailPayload> unsuccessful, int unsuccessfulMissingEmail,
        IReadOnlyList<string> participantNames, IReadOnlyList<string> winningPriceTexts) =>
        new()
        {
            Success = true,
            Winner = winner,
            WinnerMissingEmail = winnerMissingEmail,
            Unsuccessful = unsuccessful,
            UnsuccessfulMissingEmail = unsuccessfulMissingEmail,
            ParticipantNames = participantNames,
            WinningPriceTexts = winningPriceTexts
        };

    // The message never says which value was wrong, so it cannot leak award details.
    public static AwardEmailPreparationResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// The suppliers to tell that a tender was REJECTED by the Top Committee: everyone who submitted an offer.
/// The payload type has no price, winner or reason field, so a rejected-tender email cannot carry them.
/// </summary>
public sealed class RejectedEmailPreparationResult
{
    public bool Success { get; private init; }
    public string Message { get; private init; } = "";
    public IReadOnlyList<UnsuccessfulSupplierEmailPayload> Recipients { get; private init; } = [];
    /// <summary>Suppliers skipped because they have no usable email address.</summary>
    public int MissingEmail { get; private init; }

    public static RejectedEmailPreparationResult Ok(IReadOnlyList<UnsuccessfulSupplierEmailPayload> recipients, int missingEmail) =>
        new() { Success = true, Recipients = recipients, MissingEmail = missingEmail };

    public static RejectedEmailPreparationResult Fail(string message) =>
        new() { Success = false, Message = message };
}
