using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// SUPPLIER: only this supplier's own invitations and offers (SupplierProfile.UserId == me), like Areas/Supplier/Controllers.
// A supplier can NEVER get another supplier's offer, price or documents: every query below starts from the supplier's own profile id.
public sealed partial class RoleDataService
{
    private async Task<AiDataResult?> SupplierAsync(string q, string question, string userId, CancellationToken ct)
    {
        var pid = await SupplierIdAsync(userId, ct);
        if (pid == null) return null;

        var number = RecordNumber(question);
        if (number != null)
        {
            // Only a tender this supplier was invited to (or selected for).
            var t = await _db.Tenders.AsNoTracking()
                .Where(x => x.TenderNumber == number
                    && (x.Invitations.Any(i => i.SupplierProfileId == pid) || x.TenderSuppliers.Any(s => s.SupplierProfileId == pid)))
                .Select(x => new
                {
                    x.TenderNumber, x.Title, x.Description, x.Terms, x.Status, x.SubmissionDeadline,
                    MyInvitation = x.Invitations.Where(i => i.SupplierProfileId == pid).Select(i => i.Status).FirstOrDefault(),
                    MyOffer = x.Bids.Where(b => b.SupplierProfileId == pid).Select(b => b.Status).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(ct);
            if (t == null) return NotFound("TenderByNumber", "tender");

            var facts = new StringBuilder()
                .AppendLine($"Tender {t.TenderNumber}: {Cut(t.Title, 120)}")
                .AppendLine($"Status: {t.Status}. Submission deadline: {Time(t.SubmissionDeadline)} UTC.")
                .AppendLine($"My invitation: {t.MyInvitation ?? "none"}. My offer: {t.MyOffer ?? "none yet"}.")
                .AppendLine($"Description: {Cut(t.Description, 300)}")
                .AppendLine($"Terms: {Cut(t.Terms, 400)}")
                .ToString();
            return Make("TenderByNumber", null, facts);
        }

        if (Wants(q, "invit", "دعو"))
        {
            var rows = await _db.Invitations.AsNoTracking()
                .Where(i => i.SupplierProfileId == pid)
                .OrderByDescending(i => i.SentAt).Take(Count(question))
                .Select(i => new { i.Tender.TenderNumber, i.Tender.Title, i.Status, i.Tender.SubmissionDeadline, i.ExpiresAt })
                .ToListAsync(ct);
            return Make("MyInvitations", new AiTable("Your tender invitations", ["Tender", "Title", "Status", "Submission deadline", "Invitation expires"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), r.Status, Day(r.SubmissionDeadline), Day(r.ExpiresAt) }).ToList()));
        }

        if (Wants(q, "offer", "bid", "عرض", "عروض"))
        {
            var rows = await _db.Bids.AsNoTracking()
                .Where(b => b.SupplierProfileId == pid)
                .OrderByDescending(b => b.SubmittedAt ?? DateTime.MinValue).Take(Count(question))
                .Select(b => new { b.Tender.TenderNumber, b.Tender.Title, b.Status, b.TotalPrice, b.Currency, b.DeliveryDays, b.SubmittedAt })
                .ToListAsync(ct);
            return Make("MyOffers", new AiTable("Your offers", ["Tender", "Title", "Status", "Total price", "Delivery (days)", "Submitted"],
                rows.Select(r => new[] { r.TenderNumber, Cut(r.Title, 60), r.Status, Money(r.TotalPrice, r.Currency), r.DeliveryDays.ToString(), Day(r.SubmittedAt) }).ToList()));
        }

        return null;
    }
}
