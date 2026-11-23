using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

// One immutable, short-lived result per Admin. No new table or client-supplied report body.
public class AiReportSnapshotStore
{
    private readonly IMemoryCache _cache;
    public AiReportSnapshotStore(IMemoryCache cache) { _cache = cache; }
    public string Save(string owner, AiReportViewModel report)
    {
        var token = Guid.NewGuid().ToString("N");
        _cache.Set("reports-ai:" + owner, (token, JsonSerializer.Serialize(report)), TimeSpan.FromMinutes(20));
        return token;
    }
    public AiReportViewModel? Read(string owner, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return null;
        if (!_cache.TryGetValue<(string Token, string Json)>("reports-ai:" + owner, out var saved) || saved.Token != token) return null;
        return JsonSerializer.Deserialize<AiReportViewModel>(saved.Json);
    }
}
