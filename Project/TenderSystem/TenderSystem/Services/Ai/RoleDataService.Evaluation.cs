using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// EVALUATION COMMITTEE: only tenders where this user is a committee member (CommitteeMembers.UserId == me),
// the same rule Areas/Supplier/Controllers/OffersController.cs uses for bid documents.
public sealed partial class RoleDataService
{
    private async Task<AiDataResult?> CommitteeAsync(string q, string question, string userId, CancellationToken ct)
    {
        var number = RecordNumber(question);
        var wantsOffers = Wants(q, "offer", "bid", "summar", "compar", "عرض", "عروض", "ملخص", "لخص", "قارن");
        var wantsTenders = Wants(q, "tender", "مناقص", "evaluat", "تقييم");
        if (number == null && !wantsOffers && !wantsTenders) return null;

        var mine = _db.Tenders.AsNoTracking().Where(t => t.CommitteeMembers.Any(m => m.UserId == userId));

        if (number != null)
        {
            var tender = await mine.Where(t => t.TenderNumber == number)
                .Select(t => new { t.Id, t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline }).FirstOrDefaultAsync(ct);
            if (tender == null) return NotFound("TenderOffers", "tender where this user is a committee member");
            return await TenderOffersAsync(tender.Id, "TenderOffers", $"Offers of tender {tender.TenderNumber}",
                $"Tender {tender.TenderNumber}: {Cut(tender.Title, 120)}. Status: {tender.Status}. Submission deadline: {Time(tender.SubmissionDeadline)} UTC.", ct);
        }

        var tenders = await mine.OrderByDescending(t => t.CreatedAt).Take(MaxRows)
            .Select(t => new { t.Id, t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline }).ToListAsync(ct);

        // "Summarize the offers" with no number: fine when the member sits on exactly one tender, otherwise ask which one.
        if (wantsOffers && tenders.Count == 1)
        {
            var t = tenders[0];
            return await TenderOffersAsync(t.Id, "TenderOffers", $"Offers of tender {t.TenderNumber}",
                $"Tender {t.TenderNumber}: {Cut(t.Title, 120)}. Status: {t.Status}. Submission deadline: {Time(t.SubmissionDeadline)} UTC.", ct);
        }

        return Make("MyTenders", new AiTable("Tenders where you are a committee member", ["Tender", "Title", "Status", "Submission deadline"],
                tenders.Select(t => new[] { t.TenderNumber, Cut(t.Title, 60), t.Status, Day(t.SubmissionDeadline) }).ToList()),
            wantsOffers && tenders.Count > 1 ? "The user did not say which tender. Ask them to repeat the question with the tender number from the table." : "");
    }
}
