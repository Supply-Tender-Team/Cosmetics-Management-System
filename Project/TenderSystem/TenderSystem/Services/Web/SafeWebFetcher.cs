using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TenderSystem.Services.Web;

public sealed record WebPage(string Url, string Title, string Text);

/// <summary>
/// Opens ONE web page for web mode, with the protections a server needs when it fetches addresses it did not choose itself (SSRF):
///   - only https, no user name or password in the address, no unusual port;
///   - the host must be one of the Admin's allowed domains (or a subdomain), on the first address AND after every redirect (at most 3, followed by hand);
///   - the connection itself is refused when the host resolves to a private, loopback, link-local or otherwise internal address, checked at
///     connect time, so a DNS trick ("rebinding") cannot slip past an earlier check;
///   - no proxy, a time limit, a size limit (the page is cut), and only text or HTML answers are read.
/// A problem is returned as a short message; this class never throws for a bad page.
/// </summary>
public sealed class SafeWebFetcher
{
    private const int MaxRedirects = 3;
    private const int MaxTextChars = 200_000;

    // One client for the whole program, so connections are reused. The relaxed one exists only for automatic tests.
    private static readonly HttpClient Strict = CreateClient(allowPrivate: false);
    private static readonly HttpClient Relaxed = CreateClient(allowPrivate: true);

    private readonly WebOptions _options;

    static SafeWebFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);       // pages in windows-1256 (Arabic) and other old encodings

    public SafeWebFetcher(IOptions<WebOptions> options) => _options = options.Value;

    public async Task<(WebPage? Page, string? Problem)> FetchAsync(string url, IReadOnlyCollection<string> allowedDomains, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var client = _options.AllowPrivateForTests ? Relaxed : Strict;

        try
        {
            var current = url;
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                var uri = CheckAddress(current, allowedDomains, _options.AllowPrivateForTests);
                if (uri is null) return (null, "The address is not allowed.");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("text/html,text/plain;q=0.9,*/*;q=0.1");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    if (location is null) return (null, "The page redirects nowhere.");
                    current = (location.IsAbsoluteUri ? location : new Uri(uri, location)).ToString();
                    continue;                                                   // checked again at the top of the loop
                }
                if (!response.IsSuccessStatusCode) return (null, $"The page answered {(int)response.StatusCode}.");

                var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                if (mediaType is not ("text/html" or "application/xhtml+xml" or "text/plain"))
                    return (null, "The page is not a text or HTML page.");

                var bytes = await ReadLimitedAsync(response.Content, _options.PageMaxKb * 1024, timeout.Token);
                var html = Decode(bytes, response.Content.Headers.ContentType);
                var (title, text) = mediaType == "text/plain" ? ("", html.Trim()) : HtmlTextExtractor.Extract(html);
                if (text.Length > MaxTextChars) text = text[..MaxTextChars];
                if (text.Length < 40) return (null, "The page has no readable text.");
                return (new WebPage(uri.ToString(), string.IsNullOrWhiteSpace(title) ? uri.Host : title, text), null);
            }
            return (null, "The page redirects too many times.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (null, "The page took too long to answer."); }
        catch (RegexMatchTimeoutException) { return (null, "The page could not be read."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or InvalidOperationException)
        {
            return (null, "The page could not be reached.");
        }
    }

    // ------------------------------------------------------------------ the address rules

    /// <summary>The address as a <see cref="Uri"/> when it may be opened, otherwise null.</summary>
    public static Uri? CheckAddress(string? address, IReadOnlyCollection<string> allowedDomains, bool allowPrivate = false)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowPrivate && uri.Scheme == Uri.UriSchemeHttp)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (!allowPrivate && (!uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns)) return null;   // no unusual port; an IP address is never an allowed domain
        return WebDomainService.IsAllowed(uri.IdnHost, allowedDomains) ? uri : null;
    }

    /// <summary>False for every address that belongs to the company network, the computer itself or is not routable on the internet.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return false;

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0                                                          // 0.0.0.0/8
                || b[0] == 10                                                           // 10.0.0.0/8
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)                           // 100.64.0.0/10 carrier-grade NAT
                || b[0] == 127                                                          // loopback
                || (b[0] == 169 && b[1] == 254)                                         // link-local, cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)                            // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)                              // 192.0.0.0/24
                || (b[0] == 192 && b[1] == 168)                                         // 192.168.0.0/16
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))                          // benchmarking
                || b[0] >= 224);                                                        // multicast and reserved
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                                                // fc00::/7 unique local
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8));     // documentation range
        }
        return false;
    }

    // ------------------------------------------------------------------ plumbing

    private static HttpClient CreateClient(bool allowPrivate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxResponseHeadersLength = 64,
        };
        if (!allowPrivate)
        {
            // The address is checked here, at the moment of connecting, and only a checked address is used.
            handler.ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                var safe = addresses.Where(IsPublicAddress).ToArray();
                if (safe.Length == 0) throw new HttpRequestException("The host resolves to a blocked address.");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(safe, context.DnsEndPoint.Port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };       // the caller applies its own time limit
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MSWandAssistant", "1.0"));
        return client;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < maxBytes && (read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxBytes - buffer.Length)), cancellationToken)) > 0)
            buffer.Write(chunk, 0, read);
        return buffer.ToArray();                                                         // a longer page is simply cut
    }

    private static string Decode(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        Encoding encoding = Encoding.UTF8;
        var charset = contentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(charset))
        {
            try { encoding = Encoding.GetEncoding(charset); } catch (ArgumentException) { /* unknown name: UTF-8 */ }
        }
        return encoding.GetString(bytes);
    }
}
