namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (TOP-02, approved ownership of the whole voting feature).
//
// A read-only snapshot of the saved facts of an AWARDED tender. It is a data contract only:
// nothing here sends an email, calls the AI or writes to the database.
//
// SECURITY: this is commercially sensitive award information (winner and price).
// Knowing a tender id is NOT permission to read it. Every future caller must check who the user is
// and what that recipient may see (for example the supplier-result release controlled by the
// Sourcing Officer) BEFORE it uses any value of this snapshot.
// It never contains votes, member names, vote reasons, audit details, supplier account data or documents.
public sealed class AwardFactsSnapshot
{
    // Tender (Tender, TenderApproval)
    public int TenderId { get; init; }
    public string TenderNumber { get; init; } = "";
    public string TenderTitle { get; init; } = "";
    /// <summary>TenderApproval.DecidedAt of the final Top Committee decision (UTC, as stored).</summary>
    public DateTime FinalDecisionAt { get; init; }

    // Awarded supplier (Tender.AwardedSupplierId, SupplierProfile)
    public int AwardedSupplierProfileId { get; init; }
    public string SupplierCompanyName { get; init; } = "";

    // Winning bid (TenderApproval.SelectedBidId, Bid, BidItem)
    public int SelectedBidId { get; init; }
    /// <summary>Bid.TotalPrice exactly as stored. It already includes item tax, shipping and installation.</summary>
    public decimal FinalTotalPrice { get; init; }
    /// <summary>Bid.Currency exactly as stored.</summary>
    public string Currency { get; init; } = "";
    /// <summary>
    /// Sum of the stored BidItem.TaxAmount values of the winning bid, for display only.
    /// It is NOT added to the total and is NOT a verified tax rate.
    /// </summary>
    public decimal TotalRecordedTaxAmount { get; init; }
    public int DeliveryDays { get; init; }
    public int ValidityDays { get; init; }
    public string PaymentTerms { get; init; } = "";

    // Guarantee and warranty data does not exist in the database yet (no entity, column or document type).
    // Nothing is guessed: these names are the facts a future change must add before they can be used.
    public const string BankGuaranteeUnavailable = "Structured bank guarantee data is not supported yet.";
    public const string LegalWarrantyUnavailable = "Structured legal warranty data is not supported yet.";
    public const string GuaranteeDocumentsUnavailable = "Approved guarantee documents are not supported yet.";

    public IReadOnlyList<string> UnavailableFields { get; } =
        [BankGuaranteeUnavailable, LegalWarrantyUnavailable, GuaranteeDocumentsUnavailable];
}

/// <summary>The answer of AwardFactsService: facts when the award is complete and consistent, otherwise a safe message.</summary>
public sealed class AwardFactsResult
{
    public bool Success { get; private init; }
    public string Message { get; private init; } = "";
    public AwardFactsSnapshot? Facts { get; private init; }

    public static AwardFactsResult Ok(AwardFactsSnapshot facts) =>
        new() { Success = true, Facts = facts };

    // The message never says which value was wrong, so it cannot leak award details.
    public static AwardFactsResult Fail(string message) =>
        new() { Success = false, Message = message };
}
