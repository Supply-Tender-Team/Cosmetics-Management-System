using System.Text.RegularExpressions;

namespace TenderSystem.Services.Ai;

/// <summary>
/// AI-11: follow-up questions. "What about its delivery?" or "show the second one" carry no tender or request number, so the data lookup
/// (which only reads the NEW question) found nothing. This adds the right number from the earlier turns of the conversation, for the
/// LOOKUP only: the model still receives the user's original words.
///
/// It stays safe because a record number is only a FILTER inside queries that are already limited to the signed-in user
/// (see <see cref="RoleDataService"/>): a number the user may not see is "not found", exactly as if they had typed it.
/// </summary>
public static class AiFollowUp
{
    // words that point back at something said before (English; Arabic is normalised the same way as RoleDataService.Normalize)
    private static readonly string[] Pointers =
    [
        " it ", " its ", " this ", " that ", " these ", " those ", " same ", " above ", " previous ", " one ", " ones ", " them ",
        " هذا ", " هذه ", " ذلك ", " تلك ", " نفس ", " هو ", " هي ",
    ];

    private static readonly string[] Openers = ["and ", "what about ", "how about ", "also "];

    // "How do I score it?" is a help question: it must not be turned into a data lookup
    private static readonly string[] HelpOpeners = ["how do i", "how to", "how can i", "how does", "where do i", "where can i", "what is", "what does", "كيف", "اين", "ما هو", "ما هي"];

    private static readonly (string[] Words, int Index)[] Ordinals =
    [
        (["first", "1st", "الاول", "الاولى"], 0),
        (["second", "2nd", "الثاني", "الثانية"], 1),
        (["third", "3rd", "الثالث", "الثالثة"], 2),
        (["last", "latest", "الاخير", "الاخيرة"], -1),
    ];

    /// <summary>The question to use for the data lookup: the original text, plus the record number it refers to (if it is a follow-up).</summary>
    public static string Resolve(string message, IReadOnlyList<AiChatTurn> history)
    {
        if (history.Count == 0 || RoleDataService.RecordNumber(message) != null) return message;     // a typed number always wins

        var q = " " + RoleDataService.Normalize(message).Trim().TrimEnd('?', '؟', '.', '!') + " ";
        var ordinal = Ordinals.Where(o => o.Words.Any(w => q.Contains(" " + w + " "))).Select(o => (int?)o.Index).FirstOrDefault();
        if (HelpOpeners.Any(h => q.TrimStart().StartsWith(h, StringComparison.Ordinal))) return message;
        var pointsBack = ordinal != null || Pointers.Any(q.Contains) || Openers.Any(o => q.TrimStart().StartsWith(o, StringComparison.Ordinal));
        if (!pointsBack) return message;

        // "the second one" -> the second row of the last table the assistant showed (the browser lists the record numbers of that table)
        if (ordinal != null)
        {
            var listed = history.LastOrDefault(t => t.Role == "assistant" && t.Numbers is { Length: > 0 })?.Numbers;
            if (listed != null)
            {
                var valid = listed.Where(n => !string.IsNullOrWhiteSpace(n) && RoleDataService.RecordNumber(n) == RoleDataService.Normalize(n).Trim()).Take(25).ToList();
                if (valid.Count > 0)
                {
                    var i = ordinal == -1 ? valid.Count - 1 : ordinal.Value;
                    if (i >= 0 && i < valid.Count) return message + " " + valid[i];
                }
            }
        }

        // otherwise: the last record number mentioned in the conversation, newest first (the last few turns only)
        foreach (var turn in history.Reverse().Take(6))
        {
            var number = RoleDataService.RecordNumber(turn.Text);
            if (number != null) return message + " " + number;
        }
        return message;
    }
}
