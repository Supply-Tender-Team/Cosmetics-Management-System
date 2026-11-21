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
// NOT activated yet: it is registered in CommitteeVotingModule (so AwardEmailPreparationService can use it),
// but no controller, notifier or job calls it, so nothing is sent.
//
// SECURITY: the result is commercially sensitive. Having a tender id is NOT authorization.
// The caller must check the signed-in user and decide what each recipient may see (for example, external
// suppliers only after the Sourcing Officer releases the results) before using the snapshot.
public class AwardFactsService
{
    private const string NotAvailableMessage = "The award facts are not available for this tender.";

    private readonly ApplicationDbContext _db;

    public AwardFactsService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Reads the award facts. Fails (without partial data) unless the tender is Awarded, has exactly one final
    /// Top Committee approval that selected a bid of this tender, and the awarded supplier is that bid's supplier.
    /// Nothing is changed, repaired or audited.
    /// </summary>
    public async Task<AwardFactsResult> GetAwardFactsAsync(int tenderId, CancellationToken cancellationToken = default)
    {
        // 1-2. The tender exists and its saved status is Awarded.
        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.Id == tenderId)
            .Select(t => new { t.Status, t.TenderNumber, t.Title, t.AwardedSupplierId })
            .FirstOrDefaultAsync(cancellationToken);
        if (tender is null || tender.Status != Statuses.TenderStatus.Awarded || tender.AwardedSupplierId is null)
            return AwardFactsResult.Fail(NotAvailableMessage);

        // 3. The final Top Committee approval. Two rows would be ambiguous, so that is treated as invalid.
        var approvals = await _db.TenderApprovals.AsNoTracking()
            .Where(a => a.TenderId == tenderId && a.Level == Statuses.ApprovalLevel.TopCommittee)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Decision, a.SelectedBidId, a.DecidedAt })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (approvals.Count != 1 || approvals[0].Decision != Statuses.ApprovalDecision.Approved)
            return AwardFactsResult.Fail(NotAvailableMessage);

        // 4. The approval names a bid.
        if (approvals[0].SelectedBidId is not int selectedBidId)
            return AwardFactsResult.Fail(NotAvailableMessage);

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

        return AwardFactsResult.Ok(new AwardFactsSnapshot
        {
            TenderId = tenderId,
            TenderNumber = tender.TenderNumber,
            TenderTitle = tender.Title,
            FinalDecisionAt = approvals[0].DecidedAt,
            AwardedSupplierProfileId = bid.SupplierProfileId,
            SupplierCompanyName = bid.SupplierCompanyName,
            SelectedBidId = selectedBidId,
            FinalTotalPrice = bid.TotalPrice,   // stored total, never recalculated
            Currency = bid.Currency,
            TotalRecordedTaxAmount = bid.TaxTotal,   // sum of stored BidItem.TaxAmount, display only
            DeliveryDays = bid.DeliveryDays,
            ValidityDays = bid.ValidityDays,
            PaymentTerms = bid.PaymentTerms
        });
    }
}
