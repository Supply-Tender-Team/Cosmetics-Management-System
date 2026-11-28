using System.Net;
using TenderSystem.Services.Web;

namespace TenderSystem.Tests.Web;

public class SafeWebFetcherTests
{
    private static readonly string[] Allowed = ["example.com"];

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void Internal_addresses_are_not_public(string ip)
    {
        Assert.False(SafeWebFetcher.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void Internet_addresses_are_public(string ip)
    {
        Assert.True(SafeWebFetcher.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void An_allowed_https_address_is_accepted()
    {
        Assert.NotNull(SafeWebFetcher.CheckAddress("https://example.com/page", Allowed));
    }

    [Theory]
    [InlineData("http://example.com/page")]                 // not https
    [InlineData("https://other.com/page")]                  // domain not allowed
    [InlineData("https://user:pw@example.com/page")]        // user info
    [InlineData("https://example.com:8443/page")]           // unusual port
    [InlineData("https://127.0.0.1/page")]                  // an IP address
    [InlineData("not a url")]
    [InlineData(null)]
    public void Everything_else_is_refused(string? address)
    {
        Assert.Null(SafeWebFetcher.CheckAddress(address, Allowed));
    }
}
