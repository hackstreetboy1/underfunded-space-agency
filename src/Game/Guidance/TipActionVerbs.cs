using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Usa.Simulation.Commands;

namespace Usa.Game.Guidance;

/// <summary>
/// Finds the vehicle actions a tip's wording names ("open", "jettison", "turn ... off"), in any inflection, so a
/// rephrasing can be held to the actions its authored text already names. Matching is case-insensitive on whole
/// words of <see cref="AiGuardrails.Normalize"/>d text; a two-word entry is a verb and its particle with up to three
/// words between ("turn the fan off"). Deliberately broad: a false positive only costs the authored fallback.
/// </summary>
public sealed class TipActionVerbs
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string WordStart = @"(?<![\p{L}\p{N}])";
    private const string WordEnd = @"(?![\p{L}\p{N}])";

    // Within one clause: spaces and tabs only, so "turn. Off" is not one action.
    private const string ParticleGap = @"(?:[\p{Zs}\t]+[\p{L}\p{N}'’/-]+){0,3}?[\p{Zs}\t]+";

    // "Keep ring B safe" is a state, "safe ring B" an action: the bare form counts only before its object.
    private const string ObjectFollows = @"(?=[\p{Zs}\t]+(?:the|a|an|both|all|each|it|them|that|this|our|their|ring|rings|bus|OAMS|RCS|T\d+)(?![\p{L}\p{N}]))";
    private static readonly FrozenSet<string> AdjectiveLikeVerbs = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "safe");

    private static readonly FrozenDictionary<string, string[]> IrregularForms =
        new Dictionary<string, string[]> { ["throw"] = ["threw", "thrown"] }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly (string Action, Regex Pattern)[] _patterns;
    private readonly Dictionary<string, HashSet<string>> _equivalents;

    /// <param name="equivalents">Groups of actions that mean the same operation ("turn off", "switch off", "kill"); a
    /// rephrasing may swap one for another in a group. An action may sit in several groups ("shut" is off and close).</param>
    public TipActionVerbs(IEnumerable<string> actions, IEnumerable<IReadOnlyList<string>>? equivalents = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var patterns = new List<(string, Regex)>();
        foreach (var raw in actions)
        {
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length is < 1 or > 2 || words.Any(w => !w.All(char.IsLetter)))
                throw new ArgumentException($"'{raw}' must be one verb, optionally followed by one particle.", nameof(actions));

            var verb = VerbPattern(words[0], bareNeedsObject: words.Length == 1 && AdjectiveLikeVerbs.Contains(words[0]));
            var pattern = words.Length == 1 ? verb : $"{verb}{ParticleGap}{Regex.Escape(words[1])}{WordEnd}";
            patterns.Add((string.Join(' ', words).ToLowerInvariant(), new Regex(pattern, MatchOptions)));
        }
        _patterns = [.. patterns];

        _equivalents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var group in equivalents ?? [])
        {
            foreach (var member in group)
            {
                var key = member.ToLowerInvariant();
                if (!_equivalents.TryGetValue(key, out var set)) _equivalents[key] = set = new HashSet<string>(StringComparer.Ordinal);
                set.UnionWith(group.Select(g => g.ToLowerInvariant()));
            }
        }
    }

    /// <summary>Verbs that act on equipment, modeled on the advisor's imperative list plus the mission's own actions.</summary>
    public static IReadOnlyList<string> DefaultActions { get; } =
    [
        "open", "close", "shut", "arm", "disarm", "isolate", "reset", "cycle", "pull", "jettison", "vent", "fire", "deploy",
        "bypass", "disconnect", "reconnect", "enable", "disable", "activate", "deactivate", "replace", "swap", "install",
        "jumper", "damp", "burn", "retrofire", "undock", "seal", "unseal", "dump", "purge", "reroute", "trip",
        "kill", "cut", "flip", "pop", "throw", "safe",
        "turn off", "turn on", "switch off", "switch on", "power down", "power off", "power up",
    ];

    /// <summary>Wordings of one operation; a rephrasing may trade one for another but not add a new operation.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> DefaultEquivalents { get; } =
    [
        ["turn off", "switch off", "power down", "power off", "shut", "disable", "deactivate", "kill", "cut"],
        ["close", "shut"],
        ["turn on", "switch on", "power up", "enable", "activate"],
        ["open", "pull", "trip", "pop", "flip", "throw"],
        ["disarm", "safe"],
        ["vent", "dump", "purge"],
        ["fire", "burn", "retrofire"],
    ];

    public static TipActionVerbs Default { get; } = new(DefaultActions, DefaultEquivalents);

    /// <summary>The distinct actions (lower-case base form, e.g. <c>turn off</c>) the text names.</summary>
    public IReadOnlySet<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = AiGuardrails.Normalize(text);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (action, pattern) in _patterns)
        {
            if (pattern.IsMatch(normalized)) found.Add(action);
        }
        return found;
    }

    /// <summary>True when <paramref name="known"/> holds the action or one that means the same operation.</summary>
    public bool IsCovered(string action, IReadOnlySet<string> known)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(known);
        var key = action.ToLowerInvariant();
        return known.Contains(key) || (_equivalents.TryGetValue(key, out var same) && same.Overlaps(known));
    }

    private static string VerbPattern(string verb, bool bareNeedsObject)
    {
        var forms = Inflections(verb).ToList();
        if (!bareNeedsObject) return $"{WordStart}(?:{string.Join('|', forms.Select(Regex.Escape))}){WordEnd}";

        forms.RemoveAll(f => string.Equals(f, verb, StringComparison.OrdinalIgnoreCase));
        return $"{WordStart}(?:(?:{string.Join('|', forms.Select(Regex.Escape))}){WordEnd}|{Regex.Escape(verb)}{WordEnd}{ObjectFollows})";
    }

    /// <summary>Regular English inflections plus a few irregular ones, longest first; over-generating is harmless.</summary>
    private static IEnumerable<string> Inflections(string verb)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { verb, verb + "s", verb + "es", verb + "ed", verb + "ing" };
        var last = verb[^1];
        if (last == 'e')
        {
            forms.Add(verb + "d");
            forms.Add(verb[..^1] + "ing");
        }
        else if (last == 'y')
        {
            forms.Add(verb[..^1] + "ies");
            forms.Add(verb[..^1] + "ied");
        }
        else
        {
            forms.Add($"{verb}{last}ed");
            forms.Add($"{verb}{last}ing");
        }
        if (IrregularForms.TryGetValue(verb, out var irregular)) forms.UnionWith(irregular);
        return forms.OrderByDescending(f => f.Length).ThenBy(f => f, StringComparer.Ordinal);
    }
}
