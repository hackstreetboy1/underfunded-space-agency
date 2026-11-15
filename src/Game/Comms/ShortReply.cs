namespace Usa.Game.Comms;

public enum ShortReplyKind
{
    /// <summary>Anything else: the text goes to the AI.</summary>
    None,

    /// <summary>"Yes", "go ahead", "do it", "copy, go", "please".</summary>
    Affirmative,

    /// <summary>"No", "never mind", "negative", "not now".</summary>
    Negative,
}

/// <summary>
/// Recognises a short yes or no to a crew offer, so it is answered on the spot instead of being sent to the AI. Only
/// short replies made of reply words count ("yes, go ahead", "no thanks"); anything longer or with other words is
/// an order or question in its own right.
/// </summary>
public static class ShortReply
{
    public const int MaxWords = 6;

    private static readonly HashSet<string> Affirmatives = new(StringComparer.Ordinal)
    {
        "yes", "yeah", "yep", "yup", "ya", "sure", "ok", "okay", "affirmative", "please", "go", "do", "proceed", "absolutely", "definitely",
        "correct", "aye", "check", "look", "read",
    };

    private static readonly HashSet<string> AffirmativeFillers = new(StringComparer.Ordinal)
    {
        "ahead", "it", "that", "copy", "roger", "for", "please", "thanks", "thank", "you", "sounds", "good", "check", "take", "a", "look",
        "read", "go", "do", "yes", "sure", "ok", "okay", "then", "now", "and",
    };

    private static readonly HashSet<string> Negatives = new(StringComparer.Ordinal)
    {
        "no", "nope", "nah", "negative", "nevermind", "never", "cancel", "dont", "skip", "forget",
    };

    private static readonly HashSet<string> NegativeFillers = new(StringComparer.Ordinal)
    {
        "mind", "thanks", "thank", "you", "not", "now", "it", "that", "do", "copy", "need", "to", "for", "hold", "off", "on",
    };

    /// <param name="ignoredWords">Words to skip anywhere, e.g. the crew's names ("Cal, yes").</param>
    public static ShortReplyKind Classify(string text, IReadOnlyCollection<string>? ignoredWords = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = new List<string>();
        foreach (var word in CommsIntentMatcher.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ignoredWords is not null && ignoredWords.Contains(word)) continue;
            words.Add(word);
        }
        if (words.Count is 0 or > MaxWords) return ShortReplyKind.None;
        if (Matches(words, Negatives, NegativeFillers) || (words.Count == 2 && words[0] == "not" && words[1] == "now")) return ShortReplyKind.Negative;
        return Matches(words, Affirmatives, AffirmativeFillers) ? ShortReplyKind.Affirmative : ShortReplyKind.None;
    }

    private static bool Matches(List<string> words, HashSet<string> strong, HashSet<string> fillers)
    {
        var anyStrong = false;
        foreach (var word in words)
        {
            if (strong.Contains(word)) anyStrong = true;
            else if (!fillers.Contains(word)) return false;
        }
        return anyStrong;
    }
}
