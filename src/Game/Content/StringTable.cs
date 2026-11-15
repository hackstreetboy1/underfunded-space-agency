using System.Diagnostics.CodeAnalysis;
using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>
/// UI strings by dotted key: <c>&lt;section&gt;.&lt;key&gt;</c> (e.g. <c>mainMenu.continue</c>),
/// <c>tapeLabels.&lt;id&gt;</c> and <c>loadingTipPrefix</c>.
/// </summary>
public sealed class StringTable : ITextSource
{
    private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);

    public StringTable(StringsDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        foreach (var (section, values) in Sections(definition))
        {
            foreach (var (key, value) in values) _byKey[$"{section}.{key}"] = value;
        }
        foreach (var label in definition.TapeLabels) _byKey[$"tapeLabels.{label.Id}"] = label.Text;
        _byKey["loadingTipPrefix"] = definition.LoadingTipPrefix;
        Keys = _byKey.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    public StringsDefinition Definition { get; }

    /// <summary>Every key, sorted ordinally.</summary>
    public IReadOnlyList<string> Keys { get; }

    public IReadOnlyList<string> LoadingLines => Definition.LoadingLines;

    public IReadOnlyList<TapeLabel> TapeLabels => Definition.TapeLabels;

    /// <exception cref="DefinitionException">Every problem in the file at once.</exception>
    public static StringTable Load(IDataSource source)
    {
        var definition = new DefinitionLoader(source).Load<StringsDefinition>(StringsDefinition.RelativePath, StringsDefinition.SchemaTag);
        var errors = new DefinitionErrors();
        Validate(definition, StringsDefinition.RelativePath, errors);
        errors.ThrowIfAny();
        return new StringTable(definition);
    }

    public static void Validate(StringsDefinition definition, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var (section, values) in Sections(definition)) ContentChecks.TextMap(errors, file, "$." + section, values);
        ContentChecks.Texts(errors, file, "$.loadingLines", definition.LoadingLines);
        ContentChecks.Text(errors, file, "$.loadingTipPrefix", definition.LoadingTipPrefix);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < definition.TapeLabels.Count; i++)
        {
            var label = definition.TapeLabels[i];
            ContentChecks.UniqueId(errors, file, $"$.tapeLabels[{i}].id", ids, label.Id, "tape label");
            ContentChecks.Text(errors, file, $"$.tapeLabels[{i}].text", label.Text);
            ContentChecks.Text(errors, file, $"$.tapeLabels[{i}].placement", label.Placement);
        }
    }

    public bool Contains(string key) => _byKey.ContainsKey(key);

    public bool TryGet(string key, [NotNullWhen(true)] out string? value) => _byKey.TryGetValue(key, out value);

    /// <exception cref="KeyNotFoundException">The key is not in <c>content/strings.json</c>; the message says where to add it.</exception>
    public string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_byKey.TryGetValue(key, out var value)) return value;

        var dot = key.IndexOf('.', StringComparison.Ordinal);
        var where = dot > 0
            ? $"Add \"{key[(dot + 1)..]}\" to the \"{key[..dot]}\" section"
            : "Keys look like \"<section>.<key>\", e.g. \"mainMenu.continue\"";
        throw new KeyNotFoundException($"String '{key}' is not defined in {StringsDefinition.RelativePath}. {where}.");
    }

    /// <summary><see cref="Get"/> with <c>{name}</c> placeholders filled; a missing value shows as <see cref="TextTemplate.MissingValue"/>.</summary>
    public string Format(string key, IReadOnlyDictionary<string, string> values) => TextTemplate.Fill(Get(key), values);

    private static (string Section, IReadOnlyDictionary<string, string> Values)[] Sections(StringsDefinition d) =>
    [
        ("agency", d.Agency),
        ("mainMenu", d.MainMenu),
        ("missionSelect", d.MissionSelect),
        ("briefing", d.Briefing),
        ("externalView", d.ExternalView),
        ("escMenu", d.EscMenu),
        ("freezeOverlay", d.FreezeOverlay),
        ("privacy", d.Privacy),
        ("encyclopedia", d.Encyclopedia),
        ("credits", d.Credits),
        ("settings", d.Settings),
        ("map", d.Map),
        ("planner", d.Planner),
        ("workspace", d.Workspace),
        ("console", d.Console),
        ("impact", d.Impact),
        ("debrief", d.Debrief),
    ];
}
