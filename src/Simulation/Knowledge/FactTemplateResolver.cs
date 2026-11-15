using System.Text;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// Replaces <c>&lt;FACT:id&gt;</c> tokens in a dialogue template with what the speaking crew member observed
/// (or was told): value and unit, "(as of MET hh:mm)" when stale, a safe phrase when never observed.
/// A token naming an unknown fact rejects the whole template so the caller falls back to its deterministic text.
/// </summary>
public sealed class FactTemplateResolver(FactCatalog catalog, KnowledgeBase knowledge)
{
    public const string TokenStart = "<FACT:";
    public const char TokenEnd = '>';

    public FactTemplateResult Resolve(string template, CrewId speaker, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(template);
        var text = new StringBuilder(template.Length + 32);
        var facts = new List<ResolvedFact>();
        var errors = new List<string>();

        foreach (var token in Scan(template, errors))
        {
            text.Append(template, token.LiteralStart, token.LiteralLength);
            if (token.FactId is null) continue;
            if (!TryParse(token.FactId, out var id) || !catalog.TryResolve(id, out var fact))
            {
                errors.Add($"Unknown fact '{token.FactId}'.");
                continue;
            }
            var resolved = ResolveFact(fact, speaker, now);
            facts.Add(resolved);
            text.Append(resolved.Text);
        }

        return errors.Count == 0
            ? new FactTemplateResult(true, text.ToString(), facts, [])
            : new FactTemplateResult(false, "", facts, errors);
    }

    /// <summary>The raw ids inside every token, in order (for checking tokens against a response's fact refs).</summary>
    public static IReadOnlyList<string> TokensOf(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var ids = new List<string>();
        foreach (var token in Scan(template, errors: null))
        {
            if (token.FactId is not null) ids.Add(token.FactId);
        }
        return ids;
    }

    private ResolvedFact ResolveFact(FactDefinition fact, CrewId speaker, MissionTime now)
    {
        if (knowledge.LatestKnownBy(speaker, fact.Id) is not { } observation)
            return new ResolvedFact(fact.Id, null, false, catalog.Phrases.Unobserved);

        var value = FactValueFormatter.Format(fact, observation);
        if (!fact.IsStale(observation.Time, now)) return new ResolvedFact(fact.Id, observation, false, value);

        var suffix = catalog.Phrases.StaleSuffix.Replace("{met}", FactValueFormatter.HoursMinutes(observation.Time), StringComparison.Ordinal);
        return new ResolvedFact(fact.Id, observation, true, $"{value} {suffix}");
    }

    private static bool TryParse(string raw, out FactId id)
    {
        try
        {
            id = new FactId(raw);
            return true;
        }
        catch (ArgumentException)
        {
            id = default;
            return false;
        }
    }

    /// <summary>Splits a template into literal runs, each optionally followed by a token's fact id.</summary>
    private static IEnumerable<Token> Scan(string template, List<string>? errors)
    {
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf(TokenStart, position, StringComparison.Ordinal);
            if (start < 0) break;
            var end = template.IndexOf(TokenEnd, start + TokenStart.Length);
            if (end < 0)
            {
                errors?.Add($"Unterminated fact token at index {start}.");
                break;
            }
            yield return new Token(position, start - position, template[(start + TokenStart.Length)..end]);
            position = end + 1;
        }
        yield return new Token(position, template.Length - position, null);
    }

    private readonly record struct Token(int LiteralStart, int LiteralLength, string? FactId);
}
