using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// SUPPLIER: only this supplier's own invitations and offers (SupplierProfile.UserId == me), like Areas/Supplier/Controllers.
// A supplier can NEVER get another supplier's offer, price or documents: every query below starts from the supplier's own profile id.
public sealed partial class RoleDataService
{
    private async Task<AiDataResult?> SupplierRecordAsync(string question, string userId, CancellationToken ct)
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

        return null;
    }
}
