using System.Text.Json;
using TenderSystem.Areas.Shared.Models.Reports;
using TenderSystem.Services.Ai;

namespace TenderSystem.Services.Reports;

// The last AI report an Admin generated, per report type, kept in the database (AI-24) so a refresh, a later visit or a restart shows it again and
// the PDF link keeps working. Generating again for the same type replaces it. No client-supplied report body: only the server's own result is stored.
public class AiReportSnapshotStore
{
    private readonly AiSavedResultStore _saved;
    public AiReportSnapshotStore(AiSavedResultStore saved) { _saved = saved; }

    public async Task<string> SaveAsync(string owner, string type, AiReportViewModel report, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        await _saved.SaveAsync(AiSavedResultStore.Report, owner, type, token, "", JsonSerializer.Serialize(report), "", ct);
        return token;
    }

    public async Task<AiReportViewModel?> ReadAsync(string owner, string token, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return null;
        var row = await _saved.GetByTokenAsync(AiSavedResultStore.Report, owner, token, ct);
        return row is null ? null : JsonSerializer.Deserialize<AiReportViewModel>(row.ResultJson);
    }

    /// <summary>The last report of this report type, with the token of its PDF link, or null when none was generated yet.</summary>
    public async Task<(string Token, AiReportViewModel Report)?> LastAsync(string owner, string type, CancellationToken ct = default)
    {
        var row = await _saved.GetAsync(AiSavedResultStore.Report, owner, type, ct);
        var report = row is null ? null : JsonSerializer.Deserialize<AiReportViewModel>(row.ResultJson);
        return report is null ? null : (row!.Token, report);
    }
}
