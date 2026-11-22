using System.Net;
using System.Text.RegularExpressions;

namespace TenderSystem.Services.Web;

/// <summary>Gets the readable text and the title out of a web page, without any library: scripts, styles and page furniture are dropped, tags removed, entities decoded.</summary>
public static class HtmlTextExtractor
{
    // A hostile page must not be able to keep a regular expression busy: past this time a RegexMatchTimeoutException is thrown (the fetcher treats the page as unreadable).
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    private static readonly Regex Title = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled, Limit);
    private static readonly Regex Dropped = new(@"<(script|style|noscript|svg|head|nav|footer|header|form|iframe|template|select|button)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled, Limit);
    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled, Limit);
    private static readonly Regex BlockTags = new(@"</?(p|div|br|li|ul|ol|tr|table|h[1-6]|section|article|main|aside|blockquote|pre|dd|dt|dl)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]*>", RegexOptions.Compiled);

    public static (string Title, string Text) Extract(string html)
    {
        var title = "";
        var titleMatch = Title.Match(html);
        if (titleMatch.Success) title = Tidy(WebUtility.HtmlDecode(AnyTag.Replace(titleMatch.Groups[1].Value, " ")));

        var body = Comments.Replace(html, " ");
        // a block can contain a block of the same name, so the drop is repeated until nothing changes (bounded)
        for (var i = 0; i < 3; i++)
        {
            var next = Dropped.Replace(body, " ");
            if (next.Length == body.Length) break;
            body = next;
        }
        body = BlockTags.Replace(body, "\n");
        body = AnyTag.Replace(body, " ");
        body = WebUtility.HtmlDecode(body);
        body = Regex.Replace(body, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", " ");
        body = Regex.Replace(body, @"[ \t ]+", " ");
        body = Regex.Replace(body, @"\s*\n\s*(\n\s*)*", "\n").Trim();
        return (title, body);
    }

    private static string Tidy(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
