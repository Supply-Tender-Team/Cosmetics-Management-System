using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Ai;
using TenderSystem.Services.Audit;

namespace TenderSystem.Services.Web;

public sealed record WebDomainItem(int Id, string Domain, DateTime AddedAt, string AddedBy);

/// <summary>The websites an Admin allows for web mode (AI-19). Nothing outside this list is ever searched or opened.</summary>
public sealed class WebDomainService
{
    /// <summary>The query sent to the search service has a length limit, so the list is kept short.</summary>
    public const int MaxDomains = 20;

    private static readonly Regex DomainPattern = new(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;

    public WebDomainService(ApplicationDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<WebDomainItem>> ListAsync(CancellationToken ct) =>
        await _db.WebAllowedDomains.AsNoTracking().OrderBy(d => d.Domain)
            .Select(d => new WebDomainItem(d.Id, d.Domain, d.AddedAt, d.AddedBy.FullName))
            .ToListAsync(ct);

    public async Task<List<string>> NamesAsync(CancellationToken ct) =>
        await _db.WebAllowedDomains.AsNoTracking().OrderBy(d => d.Domain).Select(d => d.Domain).ToListAsync(ct);

    /// <summary>Adds a domain typed by an Admin ("https://www.example.org/path" is accepted and reduced to "example.org"). Returns a message for the Admin.</summary>
    public async Task<(bool Ok, string Message)> AddAsync(string userId, string? input, CancellationToken ct)
    {
        var domain = Normalize(input);
        if (domain is null)
            return (false, "Type a website name such as example.org (no addresses, no paths).");
        if (await _db.WebAllowedDomains.CountAsync(ct) >= MaxDomains)
            return (false, $"At most {MaxDomains} websites are allowed.");
        if (await _db.WebAllowedDomains.AnyAsync(d => d.Domain == domain, ct))
            return (false, $"{domain} is already in the list.");

        _db.WebAllowedDomains.Add(new WebAllowedDomain { Domain = domain, AddedById = userId });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WebDomainAdded", "WebAllowedDomain", domain.Length <= 50 ? domain : "domain", domain);
        return (true, $"{domain} (and its subdomains) can now be used in web mode.");
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken ct)
    {
        var domain = await _db.WebAllowedDomains.Where(d => d.Id == id).Select(d => d.Domain).FirstOrDefaultAsync(ct);
        if (domain is null) return false;
        await _db.WebAllowedDomains.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        await _audit.LogAsync("WebDomainRemoved", "WebAllowedDomain", id.ToString(), domain);
        return true;
    }

    /// <summary>"https://www.Example.org/a/b" becomes "www.example.org"; null when it is not a plain website name (IP addresses, localhost and single words are refused).</summary>
    public static string? Normalize(string? input)
    {
        var text = (input ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0 || text.Length > 200) return null;
        if (text.Contains("://")) text = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var cut = text.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) text = text[..cut];
        if (text.Contains('@') || text.Contains(':')) return null;           // no user names, no ports
        text = text.TrimEnd('.');
        if (IPAddress.TryParse(text, out _)) return null;
        return DomainPattern.IsMatch(text) ? text : null;
    }

    /// <summary>True when <paramref name="host"/> is one of the allowed domains or a subdomain of one ("docs.example.org" for "example.org"; "evilexample.org" is NOT).</summary>
    public static bool IsAllowed(string host, IEnumerable<string> domains)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return false;
        return domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }
}
