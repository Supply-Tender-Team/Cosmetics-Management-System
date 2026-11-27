using System.Text;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

// SOURCING MANAGER and TOP COMMITTEE: "analyze / summarize / compare this tender package".
// The chat gets the same safe facts as the AI summary card (AiTenderSummaryService): confirmed committee results only, calculated by
// the server, and only for the people who may see them. When nothing is waiting, it says so plainly instead of a vague "no records".
public sealed partial class RoleDataService
{
    private const int MaxPackageChars = 14000;

    private static readonly string[] AnalyzeStems =
        ["analy", "summar", "compar", "recommend", "brief me", "حلل", "تحليل", "لخص", "ملخص", "قارن", "مقارن", "توصي", "توصية"];

    // "How do I review a tender package?" is a help question, not a request to analyze data.
    private static readonly string[] HowToStems =
        ["how do i", "how to", "how can i", "what is", "what does", "where do i", "where can i", "where are", "كيف", "ما هو", "ما هي", "اين"];

    private async Task<AiDataResult?> PackageAsync(string role, string userId, string q, string question, CancellationToken ct)
    {
        if (!Wants(q, AnalyzeStems)) return null;
        var number = RecordNumber(question);
        if (number == null && Wants(q, HowToStems)) return null;

        int? tenderId;
        if (number != null)
        {
            // the number is only a filter: the access rules decide below whether this user may see that tender at all
            tenderId = await _db.Tenders.AsNoTracking().Where(t => t.TenderNumber == number).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        }
        else
        {
            var waiting = await WaitingTendersAsync(role, userId, ct);
            if (waiting.Count == 0) return await NothingWaitingAsync(role, ct);
            if (waiting.Count > 1)
            {
                var list = new AiTable("Tenders you can ask me about", ["Tender", "Title", "Status"],
                    waiting.Select(t => new[] { t.TenderNumber, Cut(t.Title, 60), t.Status }).ToList());
                return Make("TenderPackage", list, "\nMore than one tender is waiting. Ask the user which tender number they want analysed.");
            }
            tenderId = waiting[0].Id;
        }

        if (tenderId is null) return NotFound("TenderPackage", "tender");
        var package = await _summary.GetChatPackageAsync(tenderId.Value, userId, role, ct);
        if (package.State == "denied") return NotFound("TenderPackage", "tender");
        if (package.State == "waiting") return Make("TenderPackage", null, "\n" + package.Message);
        if (package.Rows.Count == 0) return Make("TenderPackage", null, $"\n{package.TenderNumber}: {package.Message}");

        var table = new AiTable($"Offers of {package.TenderNumber}", package.Columns, package.Rows);
        var extra = new StringBuilder();
        extra.AppendLine();
        extra.AppendLine($"TASK: the user wants tender {package.TenderNumber} ({Cut(package.Title, 100)}) analysed. Do it now. Do not describe system pages and do not say you cannot: write a short analysis (5 to 8 sentences) that compares the offers, using ONLY the facts below. The offers table is shown to the user separately, so do not repeat it. Copy numbers exactly, never calculate. Say what is provisional or missing and which attached files were read (the facts say; pictures and scans cannot be read). Do not name a winner: you may say which offer ranks highest only when the facts say the ranking is final.");
        extra.AppendLine();
        extra.AppendLine(package.FactsText);
        extra.AppendLine();
        extra.Append($"REMINDER: now write the analysis of {package.TenderNumber} for the user, from the facts above.");
        return Make("TenderPackage", table, extra.ToString(), MaxPackageChars);
    }

    private sealed record WaitingTender(int Id, string TenderNumber, string Title, string Status);

    /// <summary>The tenders waiting for THIS user at their level (a few at most).</summary>
    private async Task<List<WaitingTender>> WaitingTendersAsync(string role, string userId, CancellationToken ct)
    {
        var query = _db.Tenders.AsNoTracking().Where(t => t.PublishDate != null);
        query = role switch
        {
            Roles.TopCommittee => query.Where(t => t.Status == Statuses.TenderStatus.AwaitingCommitteeDecision),
            Roles.SourcingManager => query.Where(t => t.Status == Statuses.TenderStatus.AwaitingManagerReview),
            _ => query.Where(t => t.CommitteeMembers.Any(m => m.UserId == userId)
                && (t.Status == Statuses.TenderStatus.Published || t.Status == Statuses.TenderStatus.Open
                    || t.Status == Statuses.TenderStatus.Closed || t.Status == Statuses.TenderStatus.UnderEvaluation)),
        };
        return await query.OrderByDescending(t => t.CreatedAt).Take(10)
            .Select(t => new WaitingTender(t.Id, t.TenderNumber, t.Title, t.Status)).ToListAsync(ct);
    }

    /// <summary>The honest answer when nothing is waiting: what that means, what was decided lately, and what the user can do.</summary>
    private async Task<AiDataResult> NothingWaitingAsync(string role, CancellationToken ct)
    {
        var level = role switch
        {
            Roles.TopCommittee => "for the Top Committee's decision",
            Roles.SourcingManager => "for the Sourcing Manager's review",
            _ => "for this user's evaluation committee",
        };
        var sb = new StringBuilder($"\nNo tender is waiting {level} right now, so there is no package to analyse yet.\n");
        sb.AppendLine("A package becomes available when a tender reaches this level. The user can also give a tender number to look at a specific one.");

        if (role is Roles.TopCommittee or Roles.SourcingManager)
        {
            var decided = await _db.Tenders.AsNoTracking().Where(t => t.Status == Statuses.TenderStatus.Awarded)
                .OrderByDescending(t => t.CreatedAt).Take(5).Select(t => t.TenderNumber + ": " + t.Title).ToListAsync(ct);
            if (decided.Count > 0) sb.AppendLine("Tenders already awarded: " + string.Join("; ", decided.Select(d => Cut(d, 80))));
        }
        return Make("TenderPackage", null, sb.ToString());
    }
}
