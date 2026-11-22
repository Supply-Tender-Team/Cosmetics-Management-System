using System.Text;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Streaming version of OllamaChatProvider.Clean: removes "&lt;think&gt;...&lt;/think&gt;" text from an answer that arrives in pieces.
/// A tag can be split between two pieces, so a possible start of a tag is held back until the next piece shows what it is.
/// </summary>
public sealed class AiThinkFilter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    private readonly StringBuilder _held = new();
    private bool _inside;
    private bool _startedText;

    /// <summary>Takes the next piece of the answer and returns what the user may see of it (maybe nothing yet).</summary>
    public string Push(string piece)
    {
        _held.Append(piece);
        var output = new StringBuilder();
        while (_held.Length > 0)
        {
            var text = _held.ToString();
            if (_inside)
            {
                var end = text.IndexOf(Close, StringComparison.OrdinalIgnoreCase);
                if (end >= 0)
                {
                    _held.Clear().Append(text[(end + Close.Length)..]);
                    _inside = false;
                    continue;
                }
                KeepOnlyTail(text, Close);                       // still thinking: drop everything except a possible start of the closing tag
                break;
            }

            var start = text.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                Emit(output, text[..start]);
                _held.Clear().Append(text[(start + Open.Length)..]);
                _inside = true;
                continue;
            }

            var keep = PossibleTagStart(text, Open);
            Emit(output, text[..^keep]);
            _held.Clear().Append(text[^keep..]);
            break;
        }
        return output.ToString();
    }

    /// <summary>The answer is over: whatever was held back was not a tag after all (unless the model never closed its thinking).</summary>
    public string Finish()
    {
        if (_inside) { _held.Clear(); return ""; }
        var rest = _held.ToString();
        _held.Clear();
        var output = new StringBuilder();
        Emit(output, rest);
        return output.ToString();
    }

    // Spaces and line breaks before the first real word are dropped, like Clean() trims the whole answer.
    private void Emit(StringBuilder output, string text)
    {
        if (!_startedText) text = text.TrimStart();
        if (text.Length == 0) return;
        _startedText = true;
        output.Append(text);
    }

    private void KeepOnlyTail(string text, string tag)
    {
        var keep = PossibleTagStart(text, tag);
        _held.Clear().Append(text[^keep..]);
    }

    /// <summary>How many characters at the end of <paramref name="text"/> could be the beginning of <paramref name="tag"/>.</summary>
    private static int PossibleTagStart(string text, string tag)
    {
        for (var length = Math.Min(tag.Length - 1, text.Length); length > 0; length--)
        {
            if (text.AsSpan(text.Length - length).Equals(tag.AsSpan(0, length), StringComparison.OrdinalIgnoreCase))
                return length;
        }
        return 0;
    }
}
