using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using Usa.Simulation.Commands;
using Usa.Simulation.Data;

namespace Usa.Game.Advisor;

/// <summary>Root of <c>config/advisor_guardrails.json</c>: what marks an engineering answer as a recommendation.</summary>
/// <param name="MaxGapWords">Words allowed between an imperative verb and the equipment noun it acts on.</param>
/// <param name="RecommendationPhrases">Rejected anywhere in the answer ("you should", "I recommend").</param>
/// <param name="ImperativeVerbs">Rejected when they open a clause or follow a modal and act on an equipment noun.</param>
/// <param name="ImperativeLeads">Words that may precede a clause-opening verb ("First, open the breaker").</param>
/// <param name="ModalLeads">Words that turn a verb into advice ("Flight should isolate the bus").</param>
public sealed record AdvisorGuardrailsDefinition(
    string Schema,
    int MaxAnswerChars,
    int MaxGapWords,
    IReadOnlyList<string> RecommendationPhrases,
    IReadOnlyList<string> ImperativeVerbs,
    IReadOnlyList<string> EquipmentNouns,
    IReadOnlyList<string> ImperativeLeads,
    IReadOnlyList<string> ModalLeads,
    string? Notes = null) : IVersionedDefinition
{
    public const string SchemaTag = "advisor_guardrails/1";
    public const string DefaultPath = "config/advisor_guardrails.json";
}

/// <summary>Recommendation checks for "Ask the engineers" answers (GDD 9.2), compiled once from <see cref="AdvisorGuardrailsDefinition"/>.</summary>
public sealed class AdvisorGuardrails
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string WordStart = @"(?<![\p{L}\p{N}])";
    private const string WordEnd = @"(?![\p{L}\p{N}])";

    // Spaces, tabs and commas, but never a sentence end or line break: a verb and its object share one clause.
    private const string Separator = @"[\p{Zs}\t,]+";
    private const string GapWord = @"[\p{L}\p{N}'/-]+";

    private static readonly FrozenDictionary<string, string[]> IrregularParticiples = new Dictionary<string, string[]>
    {
        ["shut"] = ["shut"], ["cut"] = ["cut"], ["throw"] = ["threw", "thrown"], ["set"] = ["set"],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly Regex _recommendation;
    private readonly Regex _imperative;

    private AdvisorGuardrails(int maxAnswerChars, Regex recommendation, Regex imperative)
    {
        MaxAnswerChars = maxAnswerChars;
        _recommendation = recommendation;
        _imperative = imperative;
    }

    public int MaxAnswerChars { get; }

    public static AdvisorGuardrails Load(DefinitionLoader loader, string path = AdvisorGuardrailsDefinition.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<AdvisorGuardrailsDefinition>(path, AdvisorGuardrailsDefinition.SchemaTag), path);
    }

    /// <exception cref="DefinitionException">Empty or malformed lists, or out-of-range limits.</exception>
    public static AdvisorGuardrails Create(AdvisorGuardrailsDefinition definition, string fileLabel = AdvisorGuardrailsDefinition.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new DefinitionErrors();
        if (definition.Schema != AdvisorGuardrailsDefinition.SchemaTag)
            errors.Add(fileLabel, "$.schema", $"Expected schema '{AdvisorGuardrailsDefinition.SchemaTag}' but found '{definition.Schema}'.");
        if (definition.MaxAnswerChars < 1) errors.Add(fileLabel, "$.maxAnswerChars", "Must be at least 1.");
        if (definition.MaxGapWords is < 0 or > 12) errors.Add(fileLabel, "$.maxGapWords", "Must be between 0 and 12.");
        CheckPhrases(definition.RecommendationPhrases, "$.recommendationPhrases", fileLabel, errors);
        CheckPhrases(definition.ImperativeVerbs, "$.imperativeVerbs", fileLabel, errors);
        CheckPhrases(definition.EquipmentNouns, "$.equipmentNouns", fileLabel, errors);
        CheckPhrases(definition.ImperativeLeads, "$.imperativeLeads", fileLabel, errors);
        CheckPhrases(definition.ModalLeads, "$.modalLeads", fileLabel, errors);
        errors.ThrowIfAny();

        var recommendation = new Regex(WordStart + Alternation(definition.RecommendationPhrases) + WordEnd, MatchOptions);
        var baseVerb = WordStart + Alternation(definition.ImperativeVerbs) + WordEnd;
        var anyVerb = WordStart + Alternation(definition.ImperativeVerbs.SelectMany(Inflections).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) + WordEnd;
        var gap = $"(?:{Separator}{GapWord}){{0,{definition.MaxGapWords}}}?{Separator}";
        var equipment = Alternation(definition.EquipmentNouns) + WordEnd;
        var modal = $"{WordStart}{Alternation(definition.ModalLeads)}{Separator}(?:{GapWord}{Separator})?";

        // "Isolate the bus", "If it sags, isolate the bus", "Short answer — isolate the bus": a base-form verb opening a clause.
        var clauseStart = @"(?:^|(?<=[.!?;:,—–\r\n])|(?<=\s-))[\s""“(*•-]*(?:[0-9]+[.)]\s*)?";
        var leads = $"(?:{Alternation(definition.ImperativeLeads)}{Separator})*";
        // "Most crews would shut down the fuel cell", "consider venting the tanks": any inflection after a modal lead.
        // "Fuel cell 2 should be isolated": the passive, with the equipment first.
        var passive = $"{WordStart}{equipment}{gap}{Alternation(definition.ModalLeads)}{Separator}(?:{GapWord}{Separator})?(?:be|been|get){Separator}{anyVerb}";
        var imperative = new Regex($"{clauseStart}{leads}{baseVerb}{gap}{equipment}|{modal}{anyVerb}{gap}{equipment}|{passive}", MatchOptions);
        return new AdvisorGuardrails(definition.MaxAnswerChars, recommendation, imperative);
    }

    /// <summary>The first recommendation phrase or imperative instruction aimed at equipment; null when none.</summary>
    public string? FindRecommendation(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = NormalizeApostrophes(AiGuardrails.Normalize(text));
        var match = _recommendation.Match(normalized);
        if (!match.Success) match = _imperative.Match(normalized);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string NormalizeApostrophes(string text) => text.Replace('’', '\'').Replace('‘', '\'');

    /// <summary>
    /// Regular English inflections of a verb phrase's first word ("shut down" gives "shuts down", "shutting down",
    /// ...); over-generating is harmless because a match also needs an equipment noun.
    /// </summary>
    private static IEnumerable<string> Inflections(string phrase)
    {
        var words = phrase.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = words[0];
        var rest = words.Length > 1 ? " " + words[1] : "";
        var forms = new List<string> { verb, verb + "s", verb + "es", verb + "ed", verb + "ing" };
        var last = char.ToLowerInvariant(verb[^1]);
        if (last == 'e') forms.AddRange([verb + "d", verb[..^1] + "ing"]);
        else if (last == 'y') forms.AddRange([verb[..^1] + "ies", verb[..^1] + "ied"]);
        else forms.AddRange([$"{verb}{last}ed", $"{verb}{last}ing"]);
        if (IrregularParticiples.TryGetValue(verb, out var irregular)) forms.AddRange(irregular);
        return forms.Select(f => f + rest);
    }

    /// <summary>Longest first so "fuel cells" wins over "fuel cell"; spaces match any run of whitespace.</summary>
    private static string Alternation(IReadOnlyList<string> phrases)
    {
        var builder = new StringBuilder("(?:");
        var first = true;
        foreach (var phrase in phrases.Select(p => p.Trim()).OrderByDescending(p => p.Length).ThenBy(p => p, StringComparer.Ordinal))
        {
            if (!first) builder.Append('|');
            first = false;
            builder.Append(Regex.Escape(phrase).Replace(@"\ ", @"\s+", StringComparison.Ordinal));
        }
        return builder.Append(')').ToString();
    }

    private static void CheckPhrases(IReadOnlyList<string> values, string path, string file, DefinitionErrors errors)
    {
        if (values.Count == 0) errors.Add(file, path, "At least one entry is required.");
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value)) errors.Add(file, $"{path}[{i}]", "Entries must not be empty.");
            else if (value.Any(c => !char.IsLetter(c) && c != ' ' && c != '-' && c != '\''))
                errors.Add(file, $"{path}[{i}]", $"'{value}' may contain only letters, spaces, hyphens and apostrophes.");
        }
    }
}
