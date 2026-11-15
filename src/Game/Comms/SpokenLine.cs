using System.Collections.Frozen;

namespace Usa.Game.Comms;

/// <summary>
/// The hard limit on what one crew line sends to text-to-speech: a short radio call, billed by the character. The full data
/// stays in the transcript and the caption; only the spoken part is capped.
/// </summary>
public static class SpokenLine
{
    /// <summary>The most characters one line may send to text-to-speech.</summary>
    public const int MaxCharacters = 90;

    /// <summary>Words a spoken line must not end on: a cut after them sounds broken off.</summary>
    private static readonly FrozenSet<string> Dangling = new[]
    {
        "a", "an", "and", "at", "but", "by", "for", "from", "in", "is", "it's", "of", "on", "or", "our", "so", "that", "the",
        "their", "to", "was", "we're", "with", "your",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="text"/> held to <see cref="MaxCharacters"/>, the last resort after a line was composed to fit: whole
    /// sentences while they fit, else the first sentence cut at its last clause (comma, semicolon, dash) or, failing that,
    /// its last whole word, never after a dangling word ("and", "the"), and closed with a full stop. Unchanged when it fits.
    /// </summary>
    public static string Cap(string text, out bool trimmed)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        trimmed = text.Length > MaxCharacters;
        if (!trimmed) return text;

        var sentences = 0;
        for (var i = 0; i < text.Length && i < MaxCharacters; i++)
        {
            if (text[i] is '.' or '?' or '!' && (i + 1 == text.Length || text[i + 1] == ' ')) sentences = i + 1;
        }
        if (sentences > 0) return text[..sentences];

        // Room for the closing full stop.
        var window = text[..(MaxCharacters - 1)];
        var clause = window.LastIndexOfAny([',', ';', ':', '—', '–']);
        // A clause only when it keeps most of the window; a lone "Well," says nothing.
        if (clause > MaxCharacters / 2 && Close(window[..clause]) is { Length: > 0 } byClause) return byClause;
        // The window ends inside a word unless the text continues with a space.
        var word = text[MaxCharacters - 1] == ' ' ? window.Length : window.LastIndexOf(' ');
        return Close(word > 0 ? window[..word] : window);
    }

    /// <summary>The words with any dangling ones and trailing punctuation dropped, closed with a full stop.</summary>
    private static string Close(string words)
    {
        var kept = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (kept.Count > 1 && Dangling.Contains(kept[^1].TrimEnd(',', ';', ':', '—', '–'))) kept.RemoveAt(kept.Count - 1);
        var text = string.Join(' ', kept).TrimEnd(',', ';', ':', '—', '–', ' ', '.');
        return text.Length == 0 ? "" : text + ".";
    }
}
