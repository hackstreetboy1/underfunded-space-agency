using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Content;

/// <summary>Crew phrases by crew member and kind, with stable ids and TTS cache keys.</summary>
public sealed class CrewPhraseBook
{
    private const int CacheHashHexChars = 12;

    private readonly Dictionary<string, CrewPhrase> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<(CrewId Crew, CrewPhraseKind Kind), CrewPhrase[]> _byCrewAndKind = [];

    public CrewPhraseBook(CrewPhrasesDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var all = new List<CrewPhrase>();
        foreach (var crew in definition.Members.Keys.Order())
        {
            foreach (var entry in definition.Members[crew])
            {
                var phrase = new CrewPhrase(entry.Id, crew, entry.Category, entry.Text, CacheKeyFor(entry.Id, entry.Text));
                if (_byId.TryAdd(phrase.Id, phrase)) all.Add(phrase);
            }
        }

        foreach (var group in all.GroupBy(p => (p.Crew, p.Kind))) _byCrewAndKind[group.Key] = group.ToArray();
        All = all;
        CrewIds = definition.Members.Keys.Order().ToArray();
    }

    /// <summary>Every phrase, grouped by crew id (ordinal) in authored order.</summary>
    public IReadOnlyList<CrewPhrase> All { get; }

    public IReadOnlyList<CrewId> CrewIds { get; }

    /// <exception cref="DefinitionException">Every problem in the file at once.</exception>
    public static CrewPhraseBook Load(IDataSource source)
    {
        var definition = new DefinitionLoader(source).Load<CrewPhrasesDefinition>(CrewPhrasesDefinition.RelativePath, CrewPhrasesDefinition.SchemaTag);
        var errors = new DefinitionErrors();
        Validate(definition, CrewPhrasesDefinition.RelativePath, errors);
        errors.ThrowIfAny();
        return new CrewPhraseBook(definition);
    }

    public static void Validate(CrewPhrasesDefinition definition, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(errors);

        var kinds = ContentChecks.SnakeNames<CrewPhraseKind>();
        ContentChecks.TextMap(errors, file, "$.categories", definition.Categories);
        ContentChecks.RequiredKeys(errors, file, "$.categories", definition.Categories, kinds, "category");
        ContentChecks.KnownKeys(errors, file, "$.categories", definition.Categories, kinds, "category");
        if (definition.Members.Count == 0) errors.Add(file, "$.members", "At least one crew member is required.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (crew, phrases) in definition.Members)
        {
            var memberPath = ContentChecks.KeyPath("$.members", crew.Value);
            if (phrases.Count == 0) errors.Add(file, memberPath, "At least one phrase is required.");
            for (var i = 0; i < phrases.Count; i++)
            {
                var path = $"{memberPath}[{i}]";
                ContentChecks.UniqueId(errors, file, path + ".id", ids, phrases[i].Id, "phrase");
                ContentChecks.Text(errors, file, path + ".text", phrases[i].Text);
            }
        }
    }

    public bool TryGet(string id, [NotNullWhen(true)] out CrewPhrase? phrase) => _byId.TryGetValue(id, out phrase);

    /// <summary>The crew member's phrases of one kind in authored order; empty when there are none.</summary>
    public IReadOnlyList<CrewPhrase> For(CrewId crew, CrewPhraseKind kind) =>
        _byCrewAndKind.TryGetValue((crew, kind), out var phrases) ? phrases : [];

    /// <summary>Deterministic choice: the same <paramref name="variant"/> (e.g. a seeded draw) always gives the same line.</summary>
    public bool TryPick(CrewId crew, CrewPhraseKind kind, int variant, [NotNullWhen(true)] out CrewPhrase? phrase)
    {
        var phrases = For(crew, kind);
        if (phrases.Count == 0)
        {
            phrase = null;
            return false;
        }
        phrase = phrases[(int)((uint)variant % (uint)phrases.Count)];
        return true;
    }

    public static string CacheKeyFor(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"{id}.{Convert.ToHexStringLower(hash)[..CacheHashHexChars]}";
    }
}
