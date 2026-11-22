using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (TOP-02, approved ownership of the whole voting feature) - read-only award facts.
//
// Returns the saved facts of an AWARDED tender for a future award email. This phase is data only:
// no email, no AI, no notification, no endpoint, and no database write of any kind.
//
// Two ways a tender gets awarded, told apart ONLY by Tender.RequiresTopCommittee:
//  - true : the Top Committee awarded it. Exactly one TopCommittee approval (Approved) must exist.
//  - false: the Sourcing Manager awarded it directly. No TopCommittee approval may exist; the latest
//           Manager approval (Approved, on an approved report of this tender) is the final decision.
// A committee tender with a missing TopCommittee approval never falls back to the Manager approval.
//
// NOT activated yet: it is registered in CommitteeVotingModule (so AwardEmailPreparationService can use it),
// but no controller, notifier or job calls it, so nothing is sent.
//
// SECURITY: the result is commercially sensitive. Having a tender id is NOT authorization.
// The caller must check the signed-in user and decide what each recipient may see (for example, external
// suppliers only after the Sourcing Officer releases the results) before using the snapshot.
public class AwardFactsService
{
    private const string NotAvailableMessage = "The award facts are not available for this tender.";

    // The value stored in BidGuarantee.GuaranteeType for a bank guarantee (same text the Supplier area uses).
    private const string BankGuaranteeType = "BankGuarantee";

    private readonly ApplicationDbContext _db;

    public AwardFactsService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Reads the award facts. Fails (without partial data) unless the tender is Awarded, has one consistent final
    /// decision (Top Committee, or Manager for a direct award) that selected a bid of this tender, and the awarded
    /// supplier is that bid's supplier. Nothing is changed, repaired or audited.
    /// </summary>
    public async Task<AwardFactsResult> GetAwardFactsAsync(int tenderId, CancellationToken cancellationToken = default)
    {
        // 1-2. The tender exists and its saved status is Awarded.
        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new { t.Status, t.TenderNumber, t.Title, t.AwardedSupplierId, t.RequiresTopCommittee })
            .FirstOrDefaultAsync(cancellationToken);
        if (tender is null || tender.Status != Statuses.TenderStatus.Awarded || tender.AwardedSupplierId is null)
            return AwardFactsResult.Fail(NotAvailableMessage);

        // 3. The final decision that awarded the tender. Which approval counts depends on the workflow.
        var decision = tender.RequiresTopCommittee
            ? await GetCommitteeDecisionAsync(tenderId, cancellationToken)
            : await GetManagerDecisionAsync(tenderId, cancellationToken);

        // 4. The decision names a bid.
        if (decision is null)
            return AwardFactsResult.Fail(NotAvailableMessage);
        var selectedBidId = decision.SelectedBidId;

        // 5-7. One query: the bid must belong to this tender; its supplier and recorded tax come with it.
        var bid = await _db.Bids.AsNoTracking()
            .Where(b => b.Id == selectedBidId && b.TenderId == tenderId)
            .Select(b => new
            {
                b.SupplierProfileId,
                SupplierCompanyName = b.SupplierProfile.CompanyName,
                b.TotalPrice,
                b.Currency,
                TaxTotal = b.BidItems.Sum(i => i.TaxAmount),
                b.DeliveryDays,
                b.ValidityDays,
                b.PaymentTerms
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (bid is null || bid.SupplierProfileId != tender.AwardedSupplierId)
            return AwardFactsResult.Fail(NotAvailableMessage);

        // 8. The bank guarantee the supplier submitted with the winning bid (null unless it is clear and complete).
        var bankGuarantee = await GetBankGuaranteeAsync(selectedBidId, cancellationToken);

        return AwardFactsResult.Ok(new AwardFactsSnapshot
        {
            TenderId = tenderId,
            TenderNumber = tender.TenderNumber,
            TenderTitle = tender.Title,
            FinalDecisionAt = decision.DecidedAt,
            AwardedSupplierProfileId = bid.SupplierProfileId,
            SupplierCompanyName = bid.SupplierCompanyName,
            SelectedBidId = selectedBidId,
            FinalTotalPrice = bid.TotalPrice,   // stored total, never recalculated
            Currency = bid.Currency,
            TotalRecordedTaxAmount = bid.TaxTotal,   // sum of stored BidItem.TaxAmount, display only
            DeliveryDays = bid.DeliveryDays,
            ValidityDays = bid.ValidityDays,
            PaymentTerms = bid.PaymentTerms,
            BankGuarantee = bankGuarantee
        });
    }

    // The bank guarantee of the WINNING bid only. It is used only when exactly one BankGuarantee record exists
    // for that bid and its data is complete; missing, invalid or ambiguous data gives null (nothing is guessed).
    // Only four fields are read (amount, currency and the two dates), so personal or confidential guarantee
    // data (guarantor identity, reference number, issuer, verification code) never enters the snapshot.
    // The data is entered by the supplier: it is NOT officially verified.
    private async Task<AwardGuaranteeDetails?> GetBankGuaranteeAsync(int winningBidId, CancellationToken cancellationToken)
    {
        // Take(2) is enough to know whether there is exactly one.
        var guarantees = await _db.BidGuarantees.AsNoTracking()
            .Where(g => g.BidId == winningBidId && g.GuaranteeType == BankGuaranteeType)
            .OrderBy(g => g.Id)
            .Select(g => new { g.Amount, g.Currency, g.IssuedAt, g.ExpiresAt })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (guarantees.Count != 1)
            return null;

        var guarantee = guarantees[0];
        var currency = guarantee.Currency?.Trim();
        if (guarantee.Amount <= 0 || string.IsNullOrWhiteSpace(currency))
            return null;
        if (guarantee.IssuedAt is not DateTime issuedAt || guarantee.ExpiresAt is not DateTime expiresAt)
            return null;

        // Validity in whole days between the two dates; it must be positive.
        var validityDays = (expiresAt.Date - issuedAt.Date).Days;
        if (validityDays <= 0)
            return null;

        return new AwardGuaranteeDetails
        {
            GuaranteeType = "Bank guarantee",
            Amount = guarantee.Amount,
            Currency = currency,
            ValidityDays = validityDays
        };
    }

    // The final decision of an award: the bid it selected and when it was made.
    private sealed record FinalDecision(int SelectedBidId, DateTime DecidedAt);

    // Committee award: exactly one TopCommittee approval, and it must be Approved with a selected bid.
    // Two rows would be ambiguous, so that is treated as invalid. There is no fallback to the Manager.
    private async Task<FinalDecision?> GetCommitteeDecisionAsync(int tenderId, CancellationToken cancellationToken)
    {
        var approvals = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Decision, a.SelectedBidId, a.DecidedAt })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (approvals.Count != 1 || approvals[0].Decision != Statuses.ApprovalDecision.Approved)
            return null;
        if (approvals[0].SelectedBidId is not int selectedBidId)
            return null;

        return new FinalDecision(selectedBidId, approvals[0].DecidedAt);
    }

    // Direct award (the tender does not need the Top Committee): the latest Manager approval is the final
    // decision. It must be Approved, on a Manager-approved report of this same tender, and the selected bid
    // must be the bid that report recommends. A TopCommittee approval must NOT exist for such a tender.
    private async Task<FinalDecision?> GetManagerDecisionAsync(int tenderId, CancellationToken cancellationToken)
    {
        var hasCommitteeApproval = await _db.TenderApprovals.AsNoTracking()
            .AnyAsync(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee, cancellationToken);
        if (hasCommitteeApproval)
            return null;

        // Same "latest Manager decision" rule as the voting service: newest first, then highest id.
        var manager = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.Manager)
            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Decision,
                a.SelectedBidId,
                a.DecidedAt,
                ReportTenderId = a.Report.TenderId,
                ReportStatus = a.Report.Status,
                ReportRecommendedBidId = a.Report.RecommendedBidId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (manager is null
            || manager.Decision != Statuses.ApprovalDecision.Approved
            || manager.SelectedBidId is not int selectedBidId
            || manager.ReportTenderId != tenderId
            || manager.ReportStatus != Statuses.ReportStatus.ManagerApproved
            || manager.ReportRecommendedBidId != selectedBidId)
            return null;

        return new FinalDecision(selectedBidId, manager.DecidedAt);
    }
}
