using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// EVALUATION COMMITTEE: only tenders where this user is a committee member (CommitteeMembers.UserId == me),
// the same rule Areas/Supplier/Controllers/OffersController.cs uses for bid documents.
// Lists ("which tenders do I evaluate?") are answered by the planner tools; this file only reads ONE tender by its number.
public sealed partial class RoleDataService
{
    private async Task<AiDataResult?> CommitteeRecordAsync(string question, string userId, CancellationToken ct)
    {
        var number = RecordNumber(question);
        if (number == null) return null;

        var tender = await _db.Tenders.AsNoTracking()
            .Where(t => t.TenderNumber == number && t.CommitteeMembers.Any(m => m.UserId == userId))
            .Select(t => new { t.Id, t.TenderNumber, t.Title, t.Status, t.SubmissionDeadline }).FirstOrDefaultAsync(ct);
        if (tender == null) return NotFound("TenderOffers", "tender where this user is a committee member");

        return await TenderOffersAsync(tender.Id, "TenderOffers", $"Offers of tender {tender.TenderNumber}",
            $"Tender {tender.TenderNumber}: {Cut(tender.Title, 120)}. Status: {tender.Status}. Submission deadline: {Time(tender.SubmissionDeadline)} UTC.", ct);
    }
}
