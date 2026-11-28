using System.Globalization;
using System.Text.RegularExpressions;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Small checks that compare what the model wrote with the facts it was given (AI-15).
/// The code, not the model, decides whether a number or a name is allowed.
/// Limit: only digits are compared. A number written in words ("thirty days") is not caught.
/// </summary>
public static class AiFactCheck
{
    private static readonly Regex NumberPattern = new(@"\d+(?:[.,]\d+)*", RegexOptions.Compiled);

    /// <summary>Every number in the text, written the same way: "4,200.00" and "4200" both become "4200".</summary>
    public static List<string> Numbers(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text)) return found;
        foreach (Match m in NumberPattern.Matches(text)) found.Add(Normalize(m.Value));
        return found;
    }

    /// <summary>The numbers of <paramref name="answer"/> that do NOT appear in <paramref name="facts"/>. Empty means every number is supported.</summary>
    public static List<string> UnsupportedNumbers(string? answer, string? facts)
    {
        var allowed = new HashSet<string>(Numbers(facts));
        return Numbers(answer).Where(n => !allowed.Contains(n)).Distinct().ToList();
    }

    /// <summary>True when the text mentions any of the terms (case-insensitive). Terms shorter than 3 characters are ignored.</summary>
    public static bool MentionsAny(string? text, IEnumerable<string>? terms)
    {
        if (string.IsNullOrEmpty(text) || terms is null) return false;
        return terms.Any(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length >= 3
            && text.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the amount appears in the text as 4200, 4,200, 4200.00 or 4,200.00.</summary>
    public static bool ContainsAmount(string? text, decimal amount)
    {
        var wanted = Normalize(amount.ToString(CultureInfo.InvariantCulture));
        return Numbers(text).Contains(wanted);
    }

    private static string Normalize(string token)
    {
        var plain = token.Replace(",", "");
        return decimal.TryParse(plain, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("0.########", CultureInfo.InvariantCulture)
            : plain;
    }
}
