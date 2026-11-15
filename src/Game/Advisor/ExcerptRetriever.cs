using System.Text;
using Usa.Game.Content;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Advisor;

/// <summary>An entry and its retrieval score for one question.</summary>
public sealed record RankedEntry(EncyclopediaEntry Entry, int Score);

/// <summary>
/// Picks the few encyclopedia entries an engineering answer may draw on. Deterministic: per distinct question word,
/// title, term, first-use and body hits score; a question naming an entry's whole term or title scores a phrase
/// bonus; entries related to a strong match get a boost; ties keep authored order. Only the question is used, never
/// mission state or telemetry, and at most <see cref="MaxExcerpts"/> entries leave the client.
/// </summary>
public sealed class ExcerptRetriever
{
    public const int MaxExcerpts = 3;

    /// <summary>Backend limits (<c>app/schemas/explain.py</c>).</summary>
    public const int MaxTitleChars = 120;
    public const int MaxTextChars = 2000;

    private const int TitleWordScore = 8;
    private const int TermWordScore = 4;
    private const int FirstUseWordScore = 2;
    private const int BodyWordScore = 1;
    private const int PhraseScore = 10;
    private const int AnchorMinScore = TitleWordScore;
    private const int RelatedBoost = 3;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "after", "an", "and", "any", "are", "as", "at", "be", "been", "before", "between", "but", "by",
        "can", "could", "define", "definition", "describe", "did", "difference", "do", "does", "during", "explain",
        "for", "from", "get", "had", "happen", "happens", "has", "have", "how", "if", "in", "into", "is", "it", "its",
        "know", "matter", "matters", "mean", "meaning", "means", "me", "much", "my", "of", "on", "or", "our", "please",
        "so", "some", "tell", "than", "that", "the", "their", "them", "then", "there", "these", "they", "this", "those",
        "to", "us", "versus", "vs", "was", "we", "were", "what", "whats", "when", "where", "which", "while", "who", "why",
        "will", "with", "work", "works", "would", "you", "your",
    };

    private readonly IndexedEntry[] _index;
    private readonly Dictionary<string, int> _orderById;

    public ExcerptRetriever(Encyclopedia encyclopedia)
    {
        ArgumentNullException.ThrowIfNull(encyclopedia);
        Encyclopedia = encyclopedia;
        _index = encyclopedia.Entries.Select(Index).ToArray();
        _orderById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < _index.Length; i++) _orderById.TryAdd(_index[i].Entry.Id, i);
    }

    public Encyclopedia Encyclopedia { get; }

    /// <summary>Every entry with a positive score, best first.</summary>
    public IReadOnlyList<RankedEntry> Rank(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var words = RawWords(question).Where(w => !StopWords.Contains(w)).Select(Stem).Where(w => w.Length > 1).Distinct(StringComparer.Ordinal).ToArray();
        var phraseText = PhraseForm(question);

        var scores = new int[_index.Length];
        for (var i = 0; i < _index.Length; i++) scores[i] = BaseScore(_index[i], words, phraseText);

        var boosted = (int[])scores.Clone();
        for (var i = 0; i < _index.Length; i++)
        {
            if (scores[i] < AnchorMinScore) continue;
            foreach (var related in _index[i].Entry.Related)
            {
                if (_orderById.TryGetValue(related, out var j)) boosted[j] += RelatedBoost;
            }
        }

        var ranked = new List<(RankedEntry Ranked, int Order)>();
        for (var i = 0; i < _index.Length; i++)
        {
            if (boosted[i] > 0) ranked.Add((new RankedEntry(_index[i].Entry, boosted[i]), i));
        }
        ranked.Sort(static (a, b) => a.Ranked.Score != b.Ranked.Score ? b.Ranked.Score.CompareTo(a.Ranked.Score) : a.Order.CompareTo(b.Order));
        return ranked.Select(r => r.Ranked).ToArray();
    }

    /// <summary>The top entries for <paramref name="question"/>; empty when nothing in the encyclopedia matches.</summary>
    public IReadOnlyList<EncyclopediaEntry> Retrieve(string question, int maxEntries = MaxExcerpts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEntries, MaxExcerpts);
        return Rank(question).Take(maxEntries).Select(r => r.Entry).ToArray();
    }

    /// <summary>The excerpt sent for an entry: its title and its first-use definition followed by the body.</summary>
    public static EncyclopediaExcerpt ToExcerpt(EncyclopediaEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new EncyclopediaExcerpt(entry.Id, Clip(entry.Title, MaxTitleChars), Clip($"{entry.FirstUse} {entry.Body}", MaxTextChars));
    }

    private static int BaseScore(IndexedEntry entry, string[] words, string phraseText)
    {
        var score = 0;
        foreach (var word in words)
        {
            if (entry.TitleWords.Contains(word)) score += TitleWordScore;
            if (entry.TermWords.Contains(word)) score += TermWordScore;
            if (entry.FirstUseWords.Contains(word)) score += FirstUseWordScore;
            if (entry.BodyWords.Contains(word)) score += BodyWordScore;
        }
        foreach (var phrase in entry.Phrases)
        {
            if (phraseText.Contains(phrase, StringComparison.Ordinal))
            {
                score += PhraseScore;
                break;
            }
        }
        return score;
    }

    private static IndexedEntry Index(EncyclopediaEntry entry)
    {
        var phrases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in entry.Term.Split(['/', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Concat(entry.Title.Split(['(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                     .Append(entry.Term))
        {
            var phrase = PhraseForm(part);
            if (phrase.Trim().Length > 1) phrases.Add(phrase);
        }

        return new IndexedEntry(
            entry,
            WordSet(entry.Title),
            WordSet(entry.Term),
            WordSet(entry.FirstUse),
            WordSet(entry.Body),
            phrases.Order(StringComparer.Ordinal).ToArray());
    }

    private static HashSet<string> WordSet(string text) => new(Words(text), StringComparer.Ordinal);

    /// <summary>Lower-case stemmed words of two or more characters.</summary>
    private static IEnumerable<string> Words(string text) => RawWords(text).Select(Stem).Where(w => w.Length > 1);

    /// <summary>Lower-case words; apostrophes are dropped ("what's" -> "whats"), any other non-letter splits words.</summary>
    private static IEnumerable<string> RawWords(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Append(' '))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (c is not ('\'' or '’') && builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }
    }

    /// <summary>Plural folding so "thrusters" matches "thruster"; applied identically to questions and entries.</summary>
    private static string Stem(string word)
    {
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal)) return word[..^3] + "y";
        if (word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal) && word[^3] is 's' or 'x' or 'z' or 'h') return word[..^2];
        if (word.Length > 3 && word[^1] == 's' && word[^2] != 's') return word[..^1];
        return word;
    }

    /// <summary>" delta v " for "Delta-v": words separated by single spaces with a space at both ends.</summary>
    private static string PhraseForm(string text) => " " + string.Join(' ', Words(text)) + " ";

    private static string Clip(string text, int maxChars) => text.Length <= maxChars ? text : text[..maxChars];

    private sealed record IndexedEntry(
        EncyclopediaEntry Entry,
        HashSet<string> TitleWords,
        HashSet<string> TermWords,
        HashSet<string> FirstUseWords,
        HashSet<string> BodyWords,
        IReadOnlyList<string> Phrases);
}
