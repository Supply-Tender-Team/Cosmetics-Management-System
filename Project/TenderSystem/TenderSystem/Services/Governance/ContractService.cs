using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Governance;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Governance;

/// <summary>
/// Creates and finalizes the final contract of an awarded tender. The final approval may call CreateDraftAsync; a contract becomes Signed only when
/// a person calls MarkSignedAsync after the document was stored. The CALLER checks the role (see GOVERNANCE-HANDOFF.md).
/// Every method returns null on success or a short error text.
/// </summary>
public class ContractService
{
    private readonly ApplicationDbContext _db;

    public ContractService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Creates the Draft contract of an awarded tender from its winning offer. The amount and currency are copied now and belong to the contract
    /// afterwards. If the tender already has a live (not Cancelled) contract, that one is returned and nothing is created.
    /// </summary>
    public async Task<(Contract? Contract, string? Error)> CreateDraftAsync(int tenderId, int awardedBidId, string createdByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(createdByUserId)) return (null, "The creator is required.");
        var tender = await _db.Tenders.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenderId, ct);
        if (tender is null) return (null, "The tender was not found.");
        if (tender.Status != Statuses.TenderStatus.Awarded || tender.AwardedSupplierId is null) return (null, "Only an awarded tender can have a contract.");

        var live = await _db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.TenderId == tenderId && c.Status != Statuses.ContractStatus.Cancelled, ct);
        if (live != null) return (live, null);

        var bid = await _db.Bids.AsNoTracking().SingleOrDefaultAsync(b => b.Id == awardedBidId && b.TenderId == tenderId, ct);
        if (bid is null) return (null, "The winning offer does not belong to this tender.");
        if (bid.SupplierProfileId != tender.AwardedSupplierId) return (null, "The offer is not from the awarded supplier.");
        if (bid.TotalPrice <= 0 || string.IsNullOrWhiteSpace(bid.Currency)) return (null, "The winning offer has no valid price or currency.");

        var earlier = await _db.Contracts.AsNoTracking().CountAsync(c => c.TenderId == tenderId, ct);
        var contract = new Contract
        {
            ContractNumber = "CTR-" + tender.TenderNumber + (earlier == 0 ? "" : "-" + (earlier + 1)),
            TenderId = tenderId, AwardedBidId = bid.Id, Status = Statuses.ContractStatus.Draft,
            Amount = bid.TotalPrice, Currency = bid.Currency.Trim().ToUpperInvariant(),
            CreatedByUserId = createdByUserId, CreatedAt = DateTime.UtcNow
        };
        _db.Contracts.Add(contract);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (contract, null);
        }
        catch (DbUpdateException)
        {
            // A live contract of this tender was created at the same moment (unique index). Return that one.
            _db.Entry(contract).State = EntityState.Detached;
            var other = await _db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.TenderId == tenderId && c.Status != Statuses.ContractStatus.Cancelled, ct);
            if (other != null) return (other, null);
            throw;
        }
    }

    /// <summary>Stores the path of the uploaded contract file. Only a Draft can change.</summary>
    public async Task<string?> SetDocumentAsync(int contractId, string documentPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentPath) || documentPath.Length > 300) return "The contract document path is missing or too long.";
        var contract = await _db.Contracts.SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null) return "The contract was not found.";
        if (contract.Status != Statuses.ContractStatus.Draft) return "Only a Draft contract can change.";
        contract.DocumentPath = documentPath.Trim();
        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Draft to Signed. Needs the stored document and the signing date (UTC, not in the future). Records who finalized it.</summary>
    public async Task<string?> MarkSignedAsync(int contractId, DateTime signedAtUtc, string finalizedByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(finalizedByUserId)) return "The person who finalizes is required.";
        var contract = await _db.Contracts.SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null) return "The contract was not found.";
        if (contract.Status != Statuses.ContractStatus.Draft) return "Only a Draft contract can be signed.";
        if (string.IsNullOrWhiteSpace(contract.DocumentPath)) return "Upload the contract document first.";
        if (signedAtUtc > DateTime.UtcNow.AddDays(1)) return "The signing date cannot be in the future.";
        contract.Status = Statuses.ContractStatus.Signed;
        contract.SignedAt = signedAtUtc;
        contract.FinalizedByUserId = finalizedByUserId;
        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Draft to Cancelled, so a new Draft can be created for the tender. A Signed contract cannot be cancelled here.</summary>
    public async Task<string?> CancelDraftAsync(int contractId, CancellationToken ct = default)
    {
        var contract = await _db.Contracts.SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null) return "The contract was not found.";
        if (contract.Status != Statuses.ContractStatus.Draft) return "Only a Draft contract can be cancelled.";
        contract.Status = Statuses.ContractStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return null;
    }
}
