using System.Diagnostics.CodeAnalysis;
using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>The encyclopedia: lookup, text search, categories, related entries and first-use tooltips.</summary>
public sealed class Encyclopedia
{
    private const int TitleHitScore = 8;
    private const int TermHitScore = 4;
    private const int FirstUseHitScore = 2;
    private const int BodyHitScore = 1;

    private readonly Dictionary<string, EncyclopediaEntry> _byId;
    private readonly Dictionary<string, EncyclopediaEntry> _byTerm;

    /// <param name="definition">A validated definition (see <see cref="Load"/>).</param>
    public Encyclopedia(EncyclopediaDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        _byId = new Dictionary<string, EncyclopediaEntry>(StringComparer.Ordinal);
        _byTerm = new Dictionary<string, EncyclopediaEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in definition.Entries)
        {
            _byId.TryAdd(entry.Id, entry);
            foreach (var term in TermsOf(entry)) _byTerm.TryAdd(term, entry);
        }
    }

    public EncyclopediaDefinition Definition { get; }

    /// <summary>Entries in authored order.</summary>
    public IReadOnlyList<EncyclopediaEntry> Entries => Definition.Entries;

    /// <summary>Category id to display label, in authored order.</summary>
    public IReadOnlyDictionary<string, string> Categories => Definition.Categories;

    /// <exception cref="DefinitionException">Every problem in the file at once.</exception>
    public static Encyclopedia Load(IDataSource source)
    {
        var definition = new DefinitionLoader(source).Load<EncyclopediaDefinition>(EncyclopediaDefinition.RelativePath, EncyclopediaDefinition.SchemaTag);
        var errors = new DefinitionErrors();
        Validate(definition, EncyclopediaDefinition.RelativePath, errors);
        errors.ThrowIfAny();
        return new Encyclopedia(definition);
    }

    public static void Validate(EncyclopediaDefinition definition, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(errors);
        ContentChecks.TextMap(errors, file, "$.eraLabels", definition.EraLabels);
        ContentChecks.TextMap(errors, file, "$.categories", definition.Categories);
        if (definition.Entries.Count == 0) errors.Add(file, "$.entries", "At least one entry is required.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < definition.Entries.Count; i++)
        {
            ContentChecks.UniqueId(errors, file, $"$.entries[{i}].id", ids, definition.Entries[i].Id, "entry");
        }

        for (var i = 0; i < definition.Entries.Count; i++)
        {
            var entry = definition.Entries[i];
            var path = $"$.entries[{i}]";
            ContentChecks.Text(errors, file, path + ".title", entry.Title);
            ContentChecks.Text(errors, file, path + ".term", entry.Term);
            ContentChecks.Text(errors, file, path + ".firstUse", entry.FirstUse);
            ContentChecks.Text(errors, file, path + ".body", entry.Body);
            if (!definition.Categories.ContainsKey(entry.Category))
                errors.Add(file, path + ".category", $"Unknown category '{entry.Category}'.");
            if (!definition.EraLabels.ContainsKey(entry.Era))
                errors.Add(file, path + ".era", $"Unknown era '{entry.Era}'.");

            var related = new HashSet<string>(StringComparer.Ordinal);
            for (var r = 0; r < entry.Related.Count; r++)
            {
                var id = entry.Related[r];
                var relatedPath = $"{path}.related[{r}]";
                if (id == entry.Id) errors.Add(file, relatedPath, "An entry cannot be related to itself.");
                else if (!ids.Contains(id)) errors.Add(file, relatedPath, $"Unknown related entry '{id}'.");
                if (!related.Add(id)) errors.Add(file, relatedPath, $"Related entry '{id}' is listed twice.");
            }

            if (entry.ImageKey is { } imageKey && !IsManifestKey(imageKey))
                errors.Add(file, path + ".imageKey", $"Image key '{imageKey}' must look like '<source>:<itemId>', a key of the curated image manifest.");

            if (entry.Sources.Count == 0) errors.Add(file, path + ".sources", "Every entry needs at least one source.");
            for (var s = 0; s < entry.Sources.Count; s++)
            {
                var source = entry.Sources[s];
                var sourcePath = $"{path}.sources[{s}]";
                ContentChecks.Text(errors, file, sourcePath + ".title", source.Title);
                ContentChecks.Text(errors, file, sourcePath + ".publisher", source.Publisher);
                ContentChecks.OptionalText(errors, file, sourcePath + ".author", source.Author);
                if (source.Url is { } url && (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps))
                    errors.Add(file, sourcePath + ".url", "Source links must be absolute https URLs.");
            }
        }
    }

    /// <summary><c>&lt;source&gt;:&lt;itemId&gt;</c>, both parts non-blank and without spaces; whether the item is shipped is the screen's check.</summary>
    public static bool IsManifestKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon < key.Length - 1 && key.IndexOf(':', colon + 1) < 0 && !key.Any(char.IsWhiteSpace);
    }

    public bool TryGet(string id, [NotNullWhen(true)] out EncyclopediaEntry? entry) => _byId.TryGetValue(id, out entry);

    public EncyclopediaEntry Get(string id) =>
        TryGet(id, out var entry) ? entry : throw new KeyNotFoundException($"Unknown encyclopedia entry '{id}' ({EncyclopediaDefinition.RelativePath}).");

    public string CategoryLabel(string category) => Categories.TryGetValue(category, out var label) ? label : category;

    public string EraLabel(string era) => Definition.EraLabels.TryGetValue(era, out var label) ? label : era;

    public IReadOnlyList<EncyclopediaEntry> ByCategory(string category) =>
        Entries.Where(e => string.Equals(e.Category, category, StringComparison.Ordinal)).ToArray();

    /// <summary>The entry's related entries in authored order; unknown ids are skipped.</summary>
    public IReadOnlyList<EncyclopediaEntry> Related(string id)
    {
        if (!TryGet(id, out var entry)) return [];
        var related = new List<EncyclopediaEntry>(entry.Related.Count);
        foreach (var relatedId in entry.Related)
        {
            if (TryGet(relatedId, out var other)) related.Add(other);
        }
        return related;
    }

    /// <summary>
    /// Entries containing every word of <paramref name="query"/> (case-insensitive) in the title, term, first-use
    /// definition or body. Title and term hits rank first; ties keep authored order. An empty query lists everything.
    /// </summary>
    /// <param name="category">Restricts results to one category when given.</param>
    public IReadOnlyList<EncyclopediaEntry> Search(string? query, string? category = null)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = new List<(EncyclopediaEntry Entry, int Score, int Order)>();
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (category is not null && !string.Equals(entry.Category, category, StringComparison.Ordinal)) continue;
            var score = Score(entry, words);
            if (score > 0 || words.Length == 0) hits.Add((entry, score, i));
        }

        hits.Sort(static (a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Order.CompareTo(b.Order));
        return hits.Select(h => h.Entry).ToArray();
    }

    /// <summary>The tooltip definition for a term as written in the UI, or for an entry id.</summary>
    public bool TryGetFirstUse(string termOrId, [NotNullWhen(true)] out FirstUseDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(termOrId);
        var key = termOrId.Trim();
        if (_byId.TryGetValue(key, out var entry) || _byTerm.TryGetValue(key, out entry))
        {
            definition = new FirstUseDefinition(entry.Id, entry.Term, entry.FirstUse);
            return true;
        }
        definition = null;
        return false;
    }

    private static int Score(EncyclopediaEntry entry, string[] words)
    {
        var total = 0;
        foreach (var word in words)
        {
            var score = 0;
            if (entry.Title.Contains(word, StringComparison.OrdinalIgnoreCase)) score += TitleHitScore;
            if (entry.Term.Contains(word, StringComparison.OrdinalIgnoreCase)) score += TermHitScore;
            if (entry.FirstUse.Contains(word, StringComparison.OrdinalIgnoreCase)) score += FirstUseHitScore;
            if (entry.Body.Contains(word, StringComparison.OrdinalIgnoreCase)) score += BodyHitScore;
            if (score == 0) return 0;
            total += score;
        }
        return total;
    }

    /// <summary>The term, its title, and each part of a compound term such as "Periapsis / apoapsis".</summary>
    private static IEnumerable<string> TermsOf(EncyclopediaEntry entry)
    {
        yield return entry.Term.Trim();
        yield return entry.Title.Trim();
        var parts = entry.Term.Split(['/', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) yield break;
        foreach (var part in parts) yield return part;
    }
}
