using System.Text;
using System.Text.RegularExpressions;

namespace TenderSystem.Services.Knowledge;

/// <summary>
/// Cuts a long text into pieces ("chunks") of about <c>chunkWords</c> words (up to <c>chunkWords + overlapWords</c> when whole paragraphs are kept together). Each piece repeats the last <c>overlapWords</c> words of the
/// one before it, so a sentence that falls on the border still appears whole in one of them. Paragraphs are kept together when they fit.
/// </summary>
public static class KnowledgeChunker
{
    public static List<string> Split(string text, int chunkWords, int overlapWords)
    {
        chunkWords = Math.Max(50, chunkWords);
        overlapWords = Math.Clamp(overlapWords, 0, chunkWords / 2);

        var chunks = new List<string>();
        var current = new List<string>();      // the words of the piece being built (a "\n" entry marks a paragraph break)
        var fresh = 0;                         // words added since the last piece was cut (the repeated tail does not count)

        void Cut()
        {
            if (fresh > 0) chunks.Add(Join(current));
            current = Tail(current, overlapWords);
            fresh = 0;
        }

        foreach (var paragraph in text.Split('\n'))
        {
            var words = Regex.Matches(paragraph, @"\S+").Select(m => m.Value).ToList();
            if (words.Count == 0) continue;

            if (words.Count > chunkWords)
            {
                // a paragraph longer than one piece is cut in windows of exactly chunkWords words, each starting overlapWords before the previous one ends
                if (fresh > 0) chunks.Add(Join(current));
                var step = Math.Max(1, chunkWords - overlapWords);
                for (var start = 0; start < words.Count; start += step)
                {
                    var end = Math.Min(words.Count, start + chunkWords);
                    chunks.Add(Join(words.GetRange(start, end - start)));
                    if (end == words.Count) break;
                }
                current = Tail(words, overlapWords);
                fresh = 0;
                continue;
            }

            if (CountWords(current) + words.Count > chunkWords && fresh > 0) Cut();
            if (current.Count > 0) current.Add("\n");
            current.AddRange(words);
            fresh += words.Count;
        }

        if (fresh > 0) chunks.Add(Join(current));
        return chunks.Where(c => c.Length >= 20).ToList();
    }

    private static int CountWords(List<string> tokens) => tokens.Count(t => t != "\n");

    private static List<string> Tail(List<string> tokens, int words)
    {
        if (words == 0) return [];
        var result = new List<string>();
        var count = 0;
        for (var i = tokens.Count - 1; i >= 0 && count < words; i--)
        {
            result.Add(tokens[i]);
            if (tokens[i] != "\n") count++;
        }
        result.Reverse();
        while (result.Count > 0 && result[0] == "\n") result.RemoveAt(0);
        return result;
    }

    private static string Join(List<string> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token == "\n") { sb.Append('\n'); continue; }
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append(' ');
            sb.Append(token);
        }
        return sb.ToString().Trim();
    }
}
