using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Game.Comms;

/// <summary>What a crew line says aloud, and whether that text carries a value (lines without one may come from the phrase cache).</summary>
public readonly record struct SpokenText(string Text, bool IncludesValues, bool Trimmed);

/// <summary>
/// The spoken half of a crew line: a short radio call with only the headline, while the transcript keeps every value. One or
/// two values are said in words in the line's own sentence ("Fuel cell 2 output, point four two kilowatts."). Three or more,
/// whether written into the line (a model's dialogue that lists them) or appended after it as data, are summed up together:
/// the line's first short clause as a lead-in, then the readings out of family (one well apart from its siblings' median) or
/// off nominal, then a count or "Rest are normal." The result is composed to fit <see cref="SpokenLine.MaxCharacters"/>:
/// lower-priority parts go first (lead-in, trailing question, the second named reading), and only as a last resort is the
/// text cut, at a sentence, clause or word boundary with a full stop.
/// </summary>
internal sealed class SpokenLineComposer(CommsContent content, FactCatalog facts)
{
    /// <summary>Most readings said by name in one summary.</summary>
    private const int MaxNamed = 2;

    /// <summary>Most values a line may carry before it is summed up instead of read out.</summary>
    private const int MaxInlineValues = 2;

    /// <summary>Longest lead-in kept before a summary: a short acknowledgement, not the model's commentary.</summary>
    private const int MaxLeadInCharacters = 40;

    /// <param name="template">The line's own words, with its fact tokens.</param>
    /// <param name="resolved">Every resolved token: the template's first, then one per data fact appended after it.</param>
    /// <param name="dataCount">How many of <paramref name="resolved"/> are appended data (the last ones).</param>
    /// <param name="shortForm">Said instead when the full spoken line would exceed the cap (a refusal's reason without its opener).</param>
    public SpokenText Compose(string template, IReadOnlyList<ResolvedFact> resolved, int dataCount, string? shortForm = null)
    {
        var inline = resolved.Count - dataCount;
        var lead = resolved.Take(inline).ToArray();
        var data = resolved.Skip(inline).ToArray();
        // Most complete first; the first that fits is said.
        var candidates = new List<(string Text, bool Values)>();

        if (resolved.Count <= MaxInlineValues)
        {
            // A short read: the line's own words with their values, then any data said in words.
            var dataNames = ItemNames(data);
            var dataSaid = Join([.. data.Select((r, i) => Reading(dataNames[i], r))]);
            candidates.Add((Join(Render(template, lead), dataSaid), resolved.Count > 0));
            if (!string.IsNullOrWhiteSpace(shortForm)) candidates.Add((shortForm.Trim(), false));
            if (lead.Length > 0)
            {
                // Only the sentences that carry a value, then the first of them.
                var valueSentences = ValueSentences(template, lead);
                candidates.Add((Join([.. valueSentences, dataSaid]), true));
                if (valueSentences.Count > 0) candidates.Add((Join(valueSentences[0], dataSaid), true));
            }
            if (resolved.Count > 0)
            {
                // The model's sentence is still too long: each reading said plainly, after a short lead-in if one fits.
                var all = resolved.ToArray();
                var names = ItemNames(all);
                var plain = Join([.. all.Select((r, i) => Reading(names[i], r))]);
                candidates.Add((Join(LeadIn(template), plain), true));
                candidates.Add((plain, true));
            }
        }
        else
        {
            var readings = resolved.ToArray();
            var leadIn = LeadIn(template);
            var question = TrailingQuestion(template, leadIn);
            var summary = Summary(readings, MaxNamed, tail: true, out var values);
            candidates.Add((Join(leadIn, summary, question), values));
            candidates.Add((Join(summary, question), values));
            candidates.Add((summary, values));
            candidates.Add((Summary(readings, 1, tail: true, out _), values));
            candidates.Add((Summary(readings, 1, tail: false, out _), values));
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var (text, values) = candidates[i];
            if (text.Length > 0 && text.Length <= SpokenLine.MaxCharacters) return new SpokenText(text, values, Trimmed: i > 0);
        }
        var last = candidates.FindLast(c => c.Text.Length > 0);
        return new SpokenText(SpokenLine.Cap(last.Text ?? "", out _), last.Values, Trimmed: true);
    }

    private static string Join(params ReadOnlySpan<string> parts)
    {
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part)) kept.Add(part.Trim());
        }
        return string.Join(' ', kept);
    }

    /// <summary>The template with each token's value in words (no "as of MET" suffix: the transcript carries that).</summary>
    private string Render(string template, ResolvedFact[] lead)
    {
        var text = new System.Text.StringBuilder(template.Length);
        var position = 0;
        var index = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf(FactTemplateResolver.TokenStart, position, StringComparison.Ordinal);
            var end = start < 0 ? -1 : template.IndexOf(FactTemplateResolver.TokenEnd, start);
            if (start < 0 || end < 0) break;
            text.Append(template, position, start - position);
            if (index < lead.Length) text.Append(Value(lead[index++]));
            position = end + 1;
        }
        text.Append(template, position, template.Length - position);
        return text.ToString();
    }

    /// <summary>
    /// The line's first short clause, before any reading, as a lead-in ("Copy, Houston." from "Copy, Houston, thruster one
    /// reads ..."); empty when the words before the first value are not a whole clause or run long.
    /// </summary>
    private static string LeadIn(string template)
    {
        var first = template.IndexOf(FactTemplateResolver.TokenStart, StringComparison.Ordinal);
        var prefix = (first < 0 ? template : template[..first]).Trim();
        for (var i = 0; i < prefix.Length && i < MaxLeadInCharacters; i++)
        {
            if (i + 1 < prefix.Length && prefix[i + 1] != ' ') continue;
            var c = prefix[i];
            if (c is '.' or '!' or '?') return prefix[..(i + 1)];
            if (c is not (',' or ';' or ':' or '—' or '–')) continue;
            var clause = prefix[..i].TrimEnd(' ', '—', '–');
            // A lone "Well," says nothing: carry on to the next boundary.
            if (clause.Contains(' ', StringComparison.Ordinal)) return clause + ".";
        }
        return "";
    }

    /// <summary>A question that closes the line ("Want us to go ahead?"), which the summary must not swallow.</summary>
    private static string TrailingQuestion(string template, string leadIn)
    {
        var last = template.LastIndexOf(FactTemplateResolver.TokenEnd);
        var tail = (last < 0 ? template : template[(last + 1)..]).Trim();
        if (!tail.EndsWith('?')) return "";
        var question = Sentences(tail)[^1].TrimStart('.', ',', ';', ' ');
        return question.Length == 0 || question == leadIn || !char.IsUpper(question[0]) ? "" : question;
    }

    /// <summary>Sentences of <paramref name="text"/>, split after '.', '?' or '!' outside fact tokens.</summary>
    private static List<string> Sentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        var inToken = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == FactTemplateResolver.TokenStart[0]) inToken = true;
            else if (text[i] == FactTemplateResolver.TokenEnd) inToken = false;
            else if (!inToken && text[i] is '.' or '?' or '!' && (i + 1 == text.Length || text[i + 1] == ' '))
            {
                if (text[start..(i + 1)].Trim() is { Length: > 0 } sentence) sentences.Add(sentence);
                start = i + 1;
            }
        }
        if (start < text.Length && text[start..].Trim() is { Length: > 0 } rest) sentences.Add(rest);
        return sentences;
    }

    /// <summary>The template's sentences that carry a value, each with its values in words.</summary>
    private List<string> ValueSentences(string template, ResolvedFact[] lead)
    {
        var kept = new List<string>();
        var index = 0;
        foreach (var sentence in Sentences(template))
        {
            var tokens = FactTemplateResolver.TokensOf(sentence).Count;
            if (tokens == 0) continue;
            kept.Add(Render(sentence, lead[Math.Min(index, lead.Length)..]));
            index += tokens;
        }
        return kept;
    }

    /// <param name="named">Most notable readings said by name.</param>
    /// <param name="tail">Close with "Rest are normal." / "Two more off nominal." / "Fifteen more on your board."</param>
    /// <param name="values">The summary says at least one value.</param>
    private string Summary(ResolvedFact[] readings, int named, bool tail, out bool values)
    {
        var names = ItemNames(readings);
        var notable = Notable(readings);
        values = true;
        if (notable.Count == 0)
        {
            if (IsFamily(readings))
            {
                values = false;
                return CommsContent.Fill(content.Line(CommsLineKeys.SpokenAllNormal), ("count", SpokenWords.Count(readings.Length)));
            }
            var first = Reading(names[0], readings[0]);
            return tail ? Join(first, CommsContent.Fill(content.Line(CommsLineKeys.SpokenMore), ("count", Capitalised(SpokenWords.Count(readings.Length - 1))))) : first;
        }

        var parts = new List<string>();
        foreach (var (index, direction) in notable.Take(named))
        {
            var key = direction switch
            {
                > 0 => IsTemperature(readings[index]) ? CommsLineKeys.SpokenHot : CommsLineKeys.SpokenHigh,
                < 0 => IsTemperature(readings[index]) ? CommsLineKeys.SpokenCold : CommsLineKeys.SpokenLow,
                _ => CommsLineKeys.SpokenReading,
            };
            parts.Add(CommsContent.Fill(content.Line(key), ("label", Capitalised(names[index])), ("value", Value(readings[index]))));
        }
        if (!tail) return Join([.. parts]);
        if (notable.Count > named)
            parts.Add(CommsContent.Fill(content.Line(CommsLineKeys.SpokenMoreOff), ("count", Capitalised(SpokenWords.Count(notable.Count - named)))));
        else if (readings.Length > notable.Count)
            parts.Add(content.Line(CommsLineKeys.SpokenRestNormal));
        return Join([.. parts]);
    }

    private string Reading(string name, ResolvedFact reading) =>
        CommsContent.Fill(content.Line(CommsLineKeys.SpokenReading), ("label", Capitalised(name)), ("value", Value(reading)));

    /// <summary>The reading's value in words; the resolver's text (e.g. "not checked") when it was never observed.</summary>
    private string Value(ResolvedFact reading)
    {
        if (reading.Observation is not { } observation || !facts.TryResolve(reading.Fact, out var definition)) return reading.Text;
        if (observation.Value is null) return FactValueFormatter.Format(definition, observation).Replace(": ", ", ", StringComparison.Ordinal);
        return SpokenWords.Value(FactValueFormatter.Format(definition, observation));
    }

    /// <summary>
    /// Indexes of the readings worth saying, most out of line first, with their direction (+1 high, −1 low, 0 off nominal):
    /// qualitative readings off their nominal values, and numbers far from the median of three or more siblings (same unit,
    /// same label but for its digits): more than two and a half median absolute deviations and a fifth of the median away.
    /// </summary>
    private List<(int Index, int Direction)> Notable(ResolvedFact[] readings)
    {
        var found = new List<(int Index, int Direction, double Weight)>();
        var groups = new Dictionary<string, List<(int Index, double Value)>>(StringComparer.Ordinal);
        for (var i = 0; i < readings.Length; i++)
        {
            if (readings[i].Observation is not { } observation || !facts.TryResolve(readings[i].Fact, out var definition)) continue;
            if (observation.Value is not { } value)
            {
                if (definition.NominalValues.Count > 0 && !definition.IsNominalText(observation.Qualitative)) found.Add((i, 0, double.MaxValue));
                continue;
            }
            var family = $"{definition.Unit}|{new string(definition.Label.Where(c => !char.IsDigit(c)).ToArray())}";
            if (!groups.TryGetValue(family, out var members)) groups[family] = members = [];
            members.Add((i, value));
        }
        foreach (var members in groups.Values)
        {
            if (members.Count < 3) continue;
            var median = Median(members.Select(m => m.Value));
            var spread = Median(members.Select(m => Math.Abs(m.Value - median)));
            var threshold = Math.Max(2.5 * spread, 0.2 * Math.Abs(median));
            foreach (var (index, value) in members)
            {
                var deviation = value - median;
                if (Math.Abs(deviation) > threshold && Math.Abs(deviation) > 1e-9) found.Add((index, Math.Sign(deviation), Math.Abs(deviation)));
            }
        }
        return found.OrderByDescending(f => f.Weight).ThenBy(f => f.Index).Select(f => (f.Index, f.Direction)).ToList();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>Every reading shares one label but for its digits ("OAMS thruster 8 temperature").</summary>
    private bool IsFamily(ResolvedFact[] readings) =>
        readings.Select(r => facts.TryResolve(r.Fact, out var d) ? new string(SpokenWords.Label(d.Label).Where(c => !char.IsDigit(c)).ToArray()) : r.Fact.Value)
            .Distinct(StringComparer.Ordinal).Count() == 1;

    private bool IsTemperature(ResolvedFact reading) =>
        facts.TryResolve(reading.Fact, out var definition) && definition.Unit is "°C" or "K";

    /// <summary>
    /// Short spoken names: in a family the part that tells the readings apart, after the last shared word before it
    /// ("OAMS thruster 8 temperature" among its siblings is "thruster 8"); otherwise the whole spoken label.
    /// </summary>
    private string[] ItemNames(ResolvedFact[] readings)
    {
        var labels = readings.Select(r => (facts.TryResolve(r.Fact, out var d) ? SpokenWords.Label(d.Label) : r.Fact.Value).Split(' ')).ToArray();
        if (labels.Length < 2 || !IsFamily(readings)) return labels.Select(l => string.Join(' ', l)).ToArray();

        var prefix = 0;
        while (labels.All(l => l.Length > prefix) && labels.All(l => l[prefix] == labels[0][prefix])) prefix++;
        var suffix = 0;
        while (labels.All(l => l.Length - suffix > prefix) && labels.All(l => l[^(suffix + 1)] == labels[0][^(suffix + 1)])) suffix++;
        var keep = Math.Max(prefix - 1, 0);
        return labels.Select(l => string.Join(' ', l[keep..(l.Length - suffix)])).ToArray();
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
