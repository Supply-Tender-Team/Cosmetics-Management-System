using System.Text.RegularExpressions;

namespace TenderSystem.Services.Ai;

public enum AiPageKind { Tender, Request, Report, Bid }

/// <summary>The record a page of the system is about: its kind and database id (never trusted: RoleDataService turns it into a number and checks the user may see it).</summary>
public sealed record AiPageTarget(AiPageKind Kind, int Id);

/// <summary>
/// AI-21: "what is the user looking at?". The full-screen assistant opens in a new tab from a page of the system and reads which page that was
/// (the browser's referrer). Only pages listed here are understood; to teach it a new page add ONE line to <see cref="Pages"/>.
/// </summary>
public static class AiPageContext
{
    // (area, controller) -> what the id on that page means, and the names the id may have in the query string
    private static readonly Dictionary<(string Area, string Controller), (AiPageKind Kind, string[] Names)> Pages = new()
    {
        [("sourcing", "tenders")] = (AiPageKind.Tender, ["id", "tenderid"]),
        [("sourcing", "compliance")] = (AiPageKind.Tender, ["tenderid"]),
        [("sourcing", "offers")] = (AiPageKind.Tender, ["tenderid"]),
        [("sourcing", "evaluation")] = (AiPageKind.Tender, ["tenderid"]),
        [("sourcing", "committeebrief")] = (AiPageKind.Tender, ["id"]),
        [("supplier", "tenders")] = (AiPageKind.Tender, ["id"]),
        [("supplier", "offers")] = (AiPageKind.Tender, ["tenderid"]),
        [("requester", "requests")] = (AiPageKind.Request, ["id"]),
        [("committee", "review")] = (AiPageKind.Tender, ["id"]),
        [("manager", "reports")] = (AiPageKind.Report, ["id"]),
        [("manager", "decisions")] = (AiPageKind.Report, ["id"]),
        [("evaluation", "assessments")] = (AiPageKind.Bid, ["id"]),
    };

    /// <summary>What the browser may send: a path with an optional short query, characters that cannot carry instructions. Otherwise null.</summary>
    public static string? Clean(string? url) =>
        !string.IsNullOrEmpty(url) && url.Length <= 200 && Regex.IsMatch(url, @"^/[A-Za-z0-9/_\-.]*(\?[A-Za-z0-9=&_\-.]*)?$") ? url : null;

    public static AiPageTarget? Parse(string? url)
    {
        url = Clean(url);
        if (url is null) return null;

        var queryAt = url.IndexOf('?');
        var path = queryAt >= 0 ? url[..queryAt] : url;
        var query = queryAt >= 0 ? url[(queryAt + 1)..] : "";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;
        if (!Pages.TryGetValue((segments[0].ToLowerInvariant(), segments[1].ToLowerInvariant()), out var page)) return null;

        // an id in the path (/Details/12 or /12), otherwise one of the query names
        foreach (var s in segments.Skip(2))
            if (int.TryParse(s, out var routeId) && routeId > 0) return new AiPageTarget(page.Kind, routeId);

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && page.Names.Contains(kv[0].ToLowerInvariant()) && int.TryParse(kv[1], out var queryId) && queryId > 0)
                return new AiPageTarget(page.Kind, queryId);
        }
        return null;
    }
}
