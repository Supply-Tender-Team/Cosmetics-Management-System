using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Areas.Committee.Models.Votes;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Committee.Services;

// OWNER: Abdallah (TOP-02, approved ownership of the whole voting feature) - read-only preparation.
//
// Prepares, for one AWARDED tender, one payload for the winning supplier and one separate payload for each
// unsuccessful participating supplier. PREPARATION ONLY: it sends no email, calls no AI, creates no
// notification and writes nothing to the database (every query is AsNoTracking).
//
// NOT connected yet: nothing calls it. It is registered in CommitteeVotingModule only so a future caller
// (after Wadea's AI service and the delivery policy exist) can inject it.
//
// SECURITY: the payloads contain commercially sensitive data. Having a tender id is NOT authorization.
// The future caller must run after the decision is committed and must not call it inside the voting request.
public class AwardEmailPreparationService
{
    private const string NotAvailableMessage = "The award emails cannot be prepared for this tender.";

    private readonly ApplicationDbContext _db;
    private readonly AwardFactsService _awardFacts;

    public AwardEmailPreparationService(ApplicationDbContext db, AwardFactsService awardFacts)
    {
        _db = db;
        _awardFacts = awardFacts;
    }

    public async Task<AwardEmailPreparationResult> PrepareAsync(int tenderId, CancellationToken cancellationToken = default)
    {
        // 1. The saved, consistent award facts (fails unless the tender is Awarded with one valid Approved decision).
        var factsResult = await _awardFacts.GetAwardFactsAsync(tenderId, cancellationToken);
        if (!factsResult.Success || factsResult.Facts is null)
            return AwardEmailPreparationResult.Fail(NotAvailableMessage);
        var facts = factsResult.Facts;

        // 2. Participants: submitted, non-withdrawn offers (same rule as the manual supplier release).
        var bids = await _db.Bids.AsNoTracking()
            .Where(b => b.TenderId == tenderId && b.SubmittedAt.HasValue
                && b.Status != Statuses.BidStatus.Draft && b.Status != Statuses.BidStatus.Withdrawn)
            .OrderByDescending(b => b.SubmittedAt)
            .ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.SupplierProfileId,
                CompanyName = b.SupplierProfile.CompanyName,
                ContactPerson = b.SupplierProfile.ContactPerson,
                Email = b.SupplierProfile.User.Email
            })
            .ToListAsync(cancellationToken);

        // One recipient per supplier, even when a supplier has several offers.
        var participants = bids.DistinctBy(b => b.SupplierProfileId).ToList();
        if (!participants.Any(p => p.SupplierProfileId == facts.AwardedSupplierProfileId))
            return AwardEmailPreparationResult.Fail(NotAvailableMessage);

        // 3. Winner first, so a shared mailbox can never receive both the award and the "not selected" email.
        var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WinningSupplierEmailPayload? winner = null;
        var winnerMissingEmail = false;

        var winnerRow = participants.First(p => p.SupplierProfileId == facts.AwardedSupplierProfileId);
        var winnerEmail = CleanEmail(winnerRow.Email);
        if (winnerEmail is null)
        {
            winnerMissingEmail = true;
        }
        else
        {
            usedEmails.Add(winnerEmail);
            winner = new WinningSupplierEmailPayload
            {
                TenderId = facts.TenderId,
                TenderNumber = facts.TenderNumber,
                TenderTitle = facts.TenderTitle,
                SupplierProfileId = winnerRow.SupplierProfileId,
                SupplierCompanyName = facts.SupplierCompanyName,
                RecipientName = RecipientName(winnerRow.ContactPerson, winnerRow.CompanyName),
                RecipientEmail = winnerEmail,
                FinalTotalPrice = facts.FinalTotalPrice,
                Currency = facts.Currency,
                DeliveryDays = facts.DeliveryDays,
                ValidityDays = facts.ValidityDays,
                PaymentTerms = facts.PaymentTerms,
                // No guarantee table or column exists yet. Never derive this from the free-text terms.
                Guarantee = null
            };
        }

        // 4. Unsuccessful suppliers: their own name and the tender only. Nothing of the winner's offer is copied.
        var unsuccessful = new List<UnsuccessfulSupplierEmailPayload>();
        var unsuccessfulMissingEmail = 0;
        foreach (var participant in participants.Where(p => p.SupplierProfileId != facts.AwardedSupplierProfileId))
        {
            var email = CleanEmail(participant.Email);
            if (email is null)
            {
                unsuccessfulMissingEmail++;
                continue;
            }
            if (!usedEmails.Add(email)) continue;   // same mailbox already used: no second email

            unsuccessful.Add(new UnsuccessfulSupplierEmailPayload
            {
                TenderId = facts.TenderId,
                TenderNumber = facts.TenderNumber,
                TenderTitle = facts.TenderTitle,
                SupplierProfileId = participant.SupplierProfileId,
                SupplierCompanyName = participant.CompanyName,
                RecipientName = RecipientName(participant.ContactPerson, participant.CompanyName),
                RecipientEmail = email
            });
        }

        return AwardEmailPreparationResult.Ok(winner, winnerMissingEmail, unsuccessful, unsuccessfulMissingEmail);
    }

    private static string RecipientName(string? contactPerson, string? companyName)
    {
        if (!string.IsNullOrWhiteSpace(contactPerson)) return contactPerson.Trim();
        if (!string.IsNullOrWhiteSpace(companyName)) return companyName.Trim();
        return "Supplier";
    }

    // Returns a trimmed, well-formed address, or null when it is missing or invalid.
    private static string? CleanEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var parsed) && parsed.Address == trimmed ? trimmed : null;
    }
}
