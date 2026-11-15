using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Usa.Simulation.Data;

namespace Usa.Simulation.Commands;

/// <summary>Root of <c>config/ai_guardrails.json</c>: what AI-written text may never contain.</summary>
/// <param name="ForbiddenWords">Real names the game never says (programs, vehicles, places, people, AI vendors), matched
/// case-insensitively as whole words; an entry may be several words. Digits may follow ("Gemini8"), letters may not
/// ("titanium" is not "titan"), except a plural "s".</param>
/// <param name="Units">Unit spellings that make a number a telemetry value. Every spelling matches case-insensitively
/// ("40 KG" is as much a telemetry value as "40 kg"); a false positive only costs the deterministic fallback.</param>
/// <param name="ComponentIdPrefixes">Vehicle id prefixes such as <c>tandem/</c> that mark a raw component id. A fallback:
/// the runtime adds the loaded mission's own vehicles with <see cref="AiGuardrails.ForVehicles"/>.</param>
public sealed record AiGuardrailsDefinition(
    string Schema,
    IReadOnlyList<string> ForbiddenWords,
    IReadOnlyList<string> Units,
    IReadOnlyList<string> ComponentIdPrefixes) : IVersionedDefinition
{
    public const string SchemaTag = "ai_guardrails/1";
    public const string DefaultPath = "config/ai_guardrails.json";
}

/// <summary>
/// Text checks for AI dialogue (SCRIPTING 13.2), compiled once from <see cref="AiGuardrailsDefinition"/>. Every check
/// runs on <see cref="Normalize"/>d text, so full-width forms and invisible characters cannot hide a match; the
/// forbidden-word and component-id checks also drop accents and fold Cyrillic and Greek look-alikes to Latin.
/// Reported matches are taken from that normalised text.
/// </summary>
public sealed class AiGuardrails
{
    private static readonly Regex FactToken = new(@"<FACT:([^<>]*)>", RegexOptions.CultureInvariant);

    // Cyrillic and Greek letters that render like Latin ones, so "Gеmini" (Cyrillic е) still reads as a name.
    private static readonly FrozenDictionary<char, char> LatinLookAlikes = new Dictionary<char, char>
    {
        ['а'] = 'a', ['А'] = 'A', ['в'] = 'b', ['В'] = 'B', ['с'] = 'c', ['С'] = 'C', ['ԁ'] = 'd', ['е'] = 'e', ['Е'] = 'E',
        ['ё'] = 'e', ['Ё'] = 'E', ['һ'] = 'h', ['Н'] = 'H', ['н'] = 'h', ['і'] = 'i', ['І'] = 'I', ['ї'] = 'i', ['ј'] = 'j',
        ['Ј'] = 'J', ['к'] = 'k', ['К'] = 'K', ['ӏ'] = 'l', ['Ӏ'] = 'I', ['м'] = 'm', ['М'] = 'M', ['п'] = 'n', ['о'] = 'o',
        ['О'] = 'O', ['р'] = 'p', ['Р'] = 'P', ['ԛ'] = 'q', ['г'] = 'r', ['ѕ'] = 's', ['Ѕ'] = 'S', ['т'] = 't', ['Т'] = 'T',
        ['у'] = 'y', ['У'] = 'Y', ['ѵ'] = 'v', ['ԝ'] = 'w', ['Ԝ'] = 'W', ['х'] = 'x', ['Х'] = 'X', ['Ү'] = 'Y',
        ['α'] = 'a', ['Α'] = 'A', ['β'] = 'b', ['Β'] = 'B', ['ϲ'] = 'c', ['Ϲ'] = 'C', ['ε'] = 'e', ['Ε'] = 'E', ['Ζ'] = 'Z',
        ['η'] = 'n', ['Η'] = 'H', ['ι'] = 'i', ['Ι'] = 'I', ['ϳ'] = 'j', ['κ'] = 'k', ['Κ'] = 'K', ['Μ'] = 'M', ['ν'] = 'v',
        ['Ν'] = 'N', ['ο'] = 'o', ['Ο'] = 'O', ['ρ'] = 'p', ['Ρ'] = 'P', ['τ'] = 't', ['Τ'] = 'T', ['υ'] = 'u', ['Υ'] = 'Y',
        ['χ'] = 'x', ['Χ'] = 'X', ['ω'] = 'w', ['ı'] = 'i', ['ɑ'] = 'a', ['ɡ'] = 'g', ['ʟ'] = 'L', ['ɴ'] = 'N', ['ꜱ'] = 's',
    }.ToFrozenDictionary();

    private readonly Regex _numberWithUnit;
    private readonly Regex _forbiddenWord;
    private readonly IReadOnlyList<string> _componentPrefixes;

    private AiGuardrails(Regex numberWithUnit, Regex forbiddenWord, IReadOnlyList<string> componentPrefixes)
    {
        _numberWithUnit = numberWithUnit;
        _forbiddenWord = forbiddenWord;
        _componentPrefixes = componentPrefixes;
    }

    public static AiGuardrails Load(DefinitionLoader loader, string path = AiGuardrailsDefinition.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<AiGuardrailsDefinition>(path, AiGuardrailsDefinition.SchemaTag), path);
    }

    /// <exception cref="DefinitionException">Empty or malformed lists.</exception>
    public static AiGuardrails Create(AiGuardrailsDefinition definition, string fileLabel = AiGuardrailsDefinition.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new DefinitionErrors();
        if (definition.Schema != AiGuardrailsDefinition.SchemaTag)
            errors.Add(fileLabel, "$.schema", $"Expected schema '{AiGuardrailsDefinition.SchemaTag}' but found '{definition.Schema}'.");
        CheckList(definition.ForbiddenWords, "$.forbiddenWords", fileLabel, errors);
        CheckList(definition.Units, "$.units", fileLabel, errors);
        CheckList(definition.ComponentIdPrefixes, "$.componentIdPrefixes", fileLabel, errors);
        for (var i = 0; i < definition.ForbiddenWords.Count; i++)
        {
            var word = definition.ForbiddenWords[i];
            if (word.Any(c => !char.IsLetter(c) && c != ' ' && c != '-' && c != '\'' && c != '.'))
                errors.Add(fileLabel, $"$.forbiddenWords[{i}]", $"'{word}' may contain only letters, spaces, hyphens, apostrophes and periods.");
        }
        errors.ThrowIfAny();

        // Longest first so "elevenlabs scribe" is reported whole rather than as "elevenlabs".
        var forbidden = definition.ForbiddenWords.Select(w => FoldLookAlikes(w.Trim()))
            .OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal).ToArray();
        return new AiGuardrails(BuildUnitRegex(definition.Units), BuildWordRegex(forbidden), definition.ComponentIdPrefixes);
    }

    /// <summary>
    /// NFKC (full-width "２１２" becomes "212", "ＮＡＳＡ" becomes "NASA") with every format character removed: soft
    /// hyphens, zero-width spaces and joiners, and bidi marks. Unpaired surrogates become U+FFFD.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var composed = Compose(text, NormalizationForm.FormKC);
        if (!composed.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.Format)) return composed;

        var builder = new StringBuilder(composed.Length);
        foreach (var c in composed)
        {
            if (char.GetUnicodeCategory(c) != UnicodeCategory.Format) builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>Fact ids inside <c>&lt;FACT:id&gt;</c> tokens, in order of appearance.</summary>
    public static IReadOnlyList<string> FactTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new List<string>();
        foreach (Match match in FactToken.Matches(text)) ids.Add(match.Groups[1].Value);
        return ids;
    }

    /// <summary>The text with every fact token replaced by a space.</summary>
    public static string WithoutFactTokens(string text) => FactToken.Replace(text, " ");

    /// <summary>
    /// True when a fact token is opened but not closed properly, e.g. <c>&lt;FACT:oams.prop_kg</c>, or is written with
    /// look-alike characters (<c>＜FACT:…＞</c>) that would reach the player unresolved.
    /// </summary>
    public static bool HasMalformedFactToken(string text) =>
        Normalize(WithoutFactTokens(text)).Contains("<FACT", StringComparison.OrdinalIgnoreCase);

    /// <summary>A number followed by a unit outside fact tokens, e.g. "40 kg" or "12%"; null when none.</summary>
    public string? FindNumberWithUnit(string text)
    {
        var match = _numberWithUnit.Match(Normalize(WithoutFactTokens(text)));
        return match.Success ? match.Value : null;
    }

    /// <summary>Every number-with-unit outside fact tokens, in order of appearance.</summary>
    public IReadOnlyList<string> FindNumbersWithUnits(string text)
    {
        var found = new List<string>();
        foreach (Match match in _numberWithUnit.Matches(Normalize(WithoutFactTokens(text)))) found.Add(match.Value);
        return found;
    }

    public string? FindForbiddenWord(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = _forbiddenWord.Match(FoldLookAlikes(text));
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// These guardrails with the loaded mission's vehicles added to the component-id check, so a data-driven mission's
    /// raw ids ("<c>capsule/power/bus_a</c>") are caught too. The configured prefixes stay as a fallback.
    /// </summary>
    /// <param name="vehicleIds">Vehicle ids as in <c>data/vehicles</c>, e.g. <c>tandem</c>; each becomes the prefix <c>tandem/</c>.</param>
    public AiGuardrails ForVehicles(IEnumerable<string> vehicleIds)
    {
        ArgumentNullException.ThrowIfNull(vehicleIds);
        var prefixes = new List<string>(_componentPrefixes);
        foreach (var id in vehicleIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var prefix = id.Trim().TrimEnd('/') + "/";
            if (!prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase)) prefixes.Add(prefix);
        }
        return new AiGuardrails(_numberWithUnit, _forbiddenWord, prefixes);
    }

    /// <summary>The vehicle prefix of the first raw component id in the text, e.g. <c>tandem/</c>; null when none.</summary>
    public string? FindComponentId(string text)
    {
        var folded = FoldLookAlikes(WithoutFactTokens(text));
        foreach (var prefix in _componentPrefixes)
        {
            if (folded.Contains(prefix, StringComparison.OrdinalIgnoreCase)) return prefix;
        }
        return null;
    }

    /// <summary>Compatibility-decomposed, without format characters or combining marks, with look-alikes made Latin.</summary>
    private static string FoldLookAlikes(string text)
    {
        var decomposed = Compose(text, NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) continue;
            builder.Append(LatinLookAlikes.TryGetValue(c, out var latin) ? latin : c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Compose(string text, NormalizationForm form)
    {
        try
        {
            return text.Normalize(form);
        }
        catch (ArgumentException)
        {
            // Model output decoded from JSON may hold unpaired surrogates, which Normalize rejects.
            return ReplaceUnpairedSurrogates(text).Normalize(form);
        }
    }

    private static string ReplaceUnpairedSurrogates(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1])) i++;
            else if (char.IsSurrogate(chars[i])) chars[i] = '\uFFFD';
        }
        return new string(chars);
    }

    private static Regex BuildUnitRegex(IReadOnlyList<string> units)
    {
        // Longest first so "kWh" wins over "kW"; letter-final units must not run into a following word ("5 min" vs "5 minimal").
        var alternatives = new List<string>();
        foreach (var unit in units.Select(u => Normalize(u.Trim())).Distinct(StringComparer.Ordinal).OrderByDescending(u => u.Length).ThenBy(u => u, StringComparer.Ordinal))
        {
            var body = $"(?i:{Regex.Escape(unit)})";
            alternatives.Add(char.IsLetter(unit[^1]) ? body + @"(?!\p{L})" : body);
        }
        var pattern = $@"(?<![\p{{L}}\p{{Nd}}_])\p{{Nd}}+(?:[.,]\p{{Nd}}+)?\s*(?:{string.Join('|', alternatives)})";
        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Only a letter on either side ends a name, so "Gemini2" and "Gemini-class" match and "titanium" does not; a plural
    /// "s" is part of the name. Between the words of an entry any run of spaces or hyphens (or none) matches, and a
    /// period is optional ("Leonard F Mason").
    /// </summary>
    private static Regex BuildWordRegex(string[] words)
    {
        var builder = new StringBuilder(@"(?<!\p{L})(?:");
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0) builder.Append('|');
            builder.Append(Regex.Escape(words[i])
                .Replace(@"\ ", @"[\s-]*", StringComparison.Ordinal)
                .Replace(@"\.", @"\.?", StringComparison.Ordinal));
        }
        builder.Append(@")s?(?!\p{L})");
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void CheckList(IReadOnlyList<string> values, string path, string file, DefinitionErrors errors)
    {
        if (values.Count == 0) errors.Add(file, path, "At least one entry is required.");
        for (var i = 0; i < values.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i])) errors.Add(file, $"{path}[{i}]", "Entries must not be empty.");
        }
    }
}
