using System.Text;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Game.Comms;

/// <summary>A fully specified order the player's words may mean (no AI involved), with its player-facing text.</summary>
/// <param name="Text">What the player reads, e.g. "Read Fuel cell 2 output" or "Arm RCS ring A".</param>
/// <param name="Fact">The fact a <c>read_fact</c> candidate reads; null for any other action.</param>
/// <param name="Score">Word overlap with the player's text, 0-1.</param>
public sealed record CommsCandidate(string Text, CatalogAction Action, IReadOnlyDictionary<string, CommandArg> Args, FactId? Fact, double Score)
{
    public bool IsRead => Action.Kind == ActionKind.GlanceRead;
}

/// <summary>
/// Deterministic word matching of the player's text against what can be ordered right now: the quick commands, a
/// <c>read_fact</c> of every readable fact (by its label), parameterless actions and actions with one enum parameter
/// (one candidate per value, named by <see cref="ICommsSessionPort.DescribeCommand"/>). It backs the AI up: it turns
/// an exact quick-command or quick-reply text into its order, picks the one clear match when the model is unsure, and
/// gives clarifications chips that fit the text instead of a fixed list.
/// </summary>
public sealed class CommsIntentMatcher(ICommsSessionPort session, CommsContent content, FactCatalog facts)
{
    /// <summary>A candidate this good, and <see cref="ClearMargin"/> ahead of the next, is taken without asking.</summary>
    public const double ClearScore = 0.6;

    public const double ClearMargin = 0.2;

    /// <summary>Weaker candidates are never offered as chips.</summary>
    public const double MinScore = 0.4;

    private const int MaxEnumeratedValues = 24;
    private static readonly ActionId ReadFact = new("read_fact");

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "an", "of", "at", "is", "are", "was", "were", "be", "what", "whats", "s", "right", "now", "on", "in", "to", "me", "us",
        "you", "your", "can", "could", "would", "will", "please", "give", "tell", "how", "much", "many", "our", "we", "i", "it", "its",
        "and", "for", "with", "that", "this", "there", "do", "does", "check", "read", "look", "current", "currently", "reading", "let",
        "go", "ahead", "get", "hey", "ok", "okay", "just", "quick", "about", "up", "out", "some", "any", "value", "level", "show",
        "say", "again", "copy", "ground", "capcom", "crew", "my", "set", "take",
    };

    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.Ordinal)
    {
        ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8",
    };

    /// <summary>Words that name the same thing as a short designator, so "oxygen" meets "O2".</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal) { ["oxygen"] = "o2" };

    /// <summary>Candidates scoring at least <see cref="MinScore"/>, best first (ties in text order), at most <paramref name="max"/>.</summary>
    public IReadOnlyList<CommsCandidate> Rank(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = Words(text, isPlayerText: true);
        if (words.Count == 0) return [];

        var best = new Dictionary<string, CommsCandidate>(StringComparer.Ordinal);
        foreach (var (candidateText, action, args, fact, scoreText) in Candidates())
        {
            var score = Score(words, Words(scoreText, isPlayerText: false));
            if (score < MinScore) continue;
            var key = Key(action.Id, args);
            if (best.TryGetValue(key, out var existing) && existing.Score >= score) continue;
            best[key] = new CommsCandidate(candidateText, action, args, fact, score);
        }
        return best.Values
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Text, StringComparer.Ordinal)
            .Take(max)
            .ToArray();
    }

    /// <summary>The single clear match of <paramref name="text"/>, or null when there is none or it is a toss-up.</summary>
    public CommsCandidate? Clear(string text)
    {
        var ranked = Rank(text, 2);
        if (ranked.Count == 0 || ranked[0].Score < ClearScore) return null;
        if (ranked.Count == 1 || ranked[0].Score - ranked[1].Score >= ClearMargin) return ranked[0];

        // A close call is still settled when only the leader names exactly the numbers or letters CAPCOM said:
        // "open the breaker for thruster 8" means the T8 breaker, not the T5–T8 group that also contains an 8.
        var said = Words(text, isPlayerText: true).Where(IsDiscriminator).ToHashSet(StringComparer.Ordinal);
        return said.Count > 0 && NamesExactly(ranked[0], said) && !NamesExactly(ranked[1], said) ? ranked[0] : null;
    }

    private bool NamesExactly(CommsCandidate candidate, HashSet<string> said)
    {
        var marks = Words(candidate.Text, isPlayerText: false).Where(IsDiscriminator).ToHashSet(StringComparer.Ordinal);
        return marks.Count > 0 && marks.SetEquals(said);
    }

    /// <summary>The allowed quick command whose label is <paramref name="text"/> (ignoring case and punctuation).</summary>
    public QuickCommandDefinition? QuickCommand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = Normalize(text);
        foreach (var quick in content.QuickCommands)
        {
            if (string.Equals(Normalize(quick.Label), normalized, StringComparison.Ordinal) && Allowed(quick.Action) is not null) return quick;
        }
        return null;
    }

    /// <summary>Lowercase words, punctuation as spaces, single-spaced: the form texts are compared in.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        var space = true;
        foreach (var c in text.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (c == '\'' || c == '’')
            {
                // "don't" → "dont", "what's" → "whats".
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }
        return builder.ToString().TrimEnd();
    }

    private IEnumerable<(string Text, CatalogAction Action, IReadOnlyDictionary<string, CommandArg> Args, FactId? Fact, string ScoreText)> Candidates()
    {
        foreach (var quick in content.QuickCommands)
        {
            if (Allowed(quick.Action) is not { } action) continue;
            var args = quick.Args ?? CommandRequest.NoArgs;
            FactId? fact = null;
            if (action.Id == ReadFact)
            {
                if (!args.TryGetValue("fact", out var raw) || !CanRead(action, raw.ToCanonicalString())) continue;
                fact = new FactId(raw.ToCanonicalString());
            }
            yield return (quick.Label, action, args, fact, quick.Label);
        }

        foreach (var action in session.AllowedActions)
        {
            if (action.Id == ReadFact)
            {
                foreach (var value in action.FindParameter("fact")?.Values ?? [])
                {
                    var fact = new FactId(value);
                    if (!facts.TryResolve(fact, out var definition)) continue;
                    var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { ["fact"] = CommandArg.Of(value) };
                    yield return (CommsContent.Fill(content.Line(CommsLineKeys.ReadOffer), ("label", definition.Label)), action, args, fact, definition.Label);
                }
                continue;
            }

            if (action.Parameters.Count == 0)
            {
                var title = Title(action);
                yield return (title, action, CommandRequest.NoArgs, null, title);
                continue;
            }

            if (action.Parameters.Count != 1 || action.Parameters[0] is not { Type: ParameterType.Enum, Values: { Count: > 0 and <= MaxEnumeratedValues } values } parameter)
                continue;
            foreach (var value in values)
            {
                var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [parameter.Name] = CommandArg.Of(value) };
                var described = session.DescribeCommand(new CommandRequest(new CommandId("match"), CommandSource.Text, action.Id, null, args, false));
                yield return (described, action, args, null, described);
            }
        }
    }

    private CatalogAction? Allowed(ActionId id)
    {
        foreach (var action in session.AllowedActions)
        {
            if (action.Id == id) return action;
        }
        return null;
    }

    private static bool CanRead(CatalogAction readFact, string fact) =>
        readFact.FindParameter("fact")?.Values?.Contains(fact, StringComparer.Ordinal) == true;

    private string Title(CatalogAction action) =>
        action.Definition.TaskId is { } task ? session.DescribeTask(task) : action.Definition.Description.TrimEnd('.');

    private static string Key(ActionId action, IReadOnlyDictionary<string, CommandArg> args) =>
        string.Join('|', [action.Value, .. args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value.ToCanonicalString()}")]);

    /// <summary>
    /// Mostly how much of the candidate the text names, partly how much of the text the candidate explains. A
    /// candidate that names a different number or letter ("fuel cell 1" for "fuel cell 2") is mostly ruled out.
    /// </summary>
    private static double Score(List<string> text, List<string> candidate)
    {
        if (candidate.Count == 0) return 0;
        var matched = 0;
        foreach (var word in candidate)
        {
            if (text.Contains(word, StringComparer.Ordinal)) matched++;
        }
        if (matched == 0) return 0;
        var score = 0.5 * matched / candidate.Count + 0.5 * Math.Min(1.0, (double)matched / text.Count);

        var candidateMarks = candidate.Where(IsDiscriminator).ToArray();
        if (candidateMarks.Length > 0 && !candidateMarks.Any(m => text.Contains(m, StringComparer.Ordinal)))
            score *= text.Any(IsDiscriminator) ? 0.4 : 0.85;
        return score;
    }

    /// <summary>A number, a single letter or a letter-number tag (<c>2</c>, <c>b</c>, <c>t5</c>): it tells siblings apart.</summary>
    private static bool IsDiscriminator(string word) => word.Length == 1 || word.Any(char.IsDigit);

    private List<string> Words(string text, bool isPlayerText)
    {
        var raw = text.Split([' ', ',', '.', '?', '!', ';', ':', '-', '–', '/', '(', ')', '"', '_'], StringSplitOptions.RemoveEmptyEntries);
        var words = new List<string>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var original = raw[i].Replace("'", "", StringComparison.Ordinal).Replace("’", "", StringComparison.Ordinal);
            if (original.Length == 0) continue;
            var lower = original.ToLowerInvariant();
            var word = NumberWords.TryGetValue(lower, out var digit) ? digit : lower.Any(char.IsDigit) ? Tag(lower) : Stem(lower);
            if (Aliases.TryGetValue(word, out var alias)) word = alias;
            // "a" is an article ("read a gauge") unless it names something ("ring A", "... ring a").
            if (word == "a" && !(original == "A" || i == raw.Length - 1)) continue;
            if (word != "a" && StopWords.Contains(word)) continue;
            if (isPlayerText && IsCrewName(word)) continue;
            if (!isPlayerText && words.Contains(word, StringComparer.Ordinal)) continue;
            words.Add(word);
        }
        return words;
    }

    private bool IsCrewName(string word)
    {
        foreach (var member in session.Crew)
        {
            if (!content.TryGetProfile(member.Id, out var profile)) continue;
            foreach (var part in profile.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(part, word, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A designator as one exact word: letters then digits keep their letters ("T08" → "t8", "CO2" → "co2", so "O2" never
    /// meets "CO2"); a bare number loses leading zeros ("08" → "8").
    /// </summary>
    private static string Tag(string word)
    {
        var split = 0;
        while (split < word.Length && char.IsLetter(word[split])) split++;
        if (split < word.Length && word[split..].All(char.IsDigit)) return split == 1 && word[0] == 't' ? Digits(word[split..]) : word[..split] + Digits(word[split..]);
        return Digits(new string(word.Where(char.IsDigit).ToArray()));
    }

    private static string Digits(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    /// <summary>Plural to singular, so "temps" meets "temp" and "breakers" meets "breaker".</summary>
    private static string Stem(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;
}
