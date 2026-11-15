using System.Diagnostics.CodeAnalysis;
using System.Text;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// Everything comms says without the AI: line templates and refusal reasons (<c>comms_templates.json</c>), crew
/// openers (<see cref="ICrewPhraseSource"/>, loaded and validated by the content library) and crew profiles
/// (<c>crew.json</c>). Templates are checked at load: no digits anywhere, and only the documented placeholders, so a
/// fallback can never present an invented value.
/// </summary>
public sealed class CommsContent
{
    public const string DefaultReason = "default";

    /// <summary>
    /// Placeholders a template may use; <c>{fact}</c> becomes a <c>&lt;FACT:id&gt;</c> token. <c>{value}</c> and <c>{count}</c> are
    /// filled with words only (the spoken read-out), never digits.
    /// </summary>
    public static readonly IReadOnlySet<string> Placeholders = new HashSet<string>(StringComparer.Ordinal) { "name", "task", "action", "label", "fact", "value", "count" };

    private readonly CommsTemplatesFile _templates;
    private readonly ICrewPhraseSource _phrases;
    private readonly Dictionary<CrewId, CrewProfile> _profiles;
    private readonly Dictionary<string, QuickCommandDefinition> _quickCommands = new(StringComparer.Ordinal);

    private CommsContent(CommsTemplatesFile templates, ICrewPhraseSource phrases, IReadOnlyList<CrewProfile> profiles)
    {
        _templates = templates;
        _phrases = phrases;
        _profiles = profiles.ToDictionary(p => p.Id);
        Profiles = profiles;
        foreach (var command in templates.QuickCommands) _quickCommands[command.Id] = command;
    }

    /// <summary>Crew profiles in id order.</summary>
    public IReadOnlyList<CrewProfile> Profiles { get; }

    /// <summary>The quick-command row in authored order.</summary>
    public IReadOnlyList<QuickCommandDefinition> QuickCommands => _templates.QuickCommands;

    /// <exception cref="DefinitionException">Every problem in the templates and crew files at once.</exception>
    public static CommsContent Load(IDataSource source, ICrewPhraseSource phrases)
    {
        ArgumentNullException.ThrowIfNull(source);
        var loader = new DefinitionLoader(source);
        return Create(
            loader.Load<CommsTemplatesFile>(CommsTemplatesFile.RelativePath, CommsTemplatesFile.SchemaTag),
            phrases,
            CrewProfileReader.Read(source));
    }

    /// <exception cref="DefinitionException">Every problem at once.</exception>
    public static CommsContent Create(CommsTemplatesFile templates, ICrewPhraseSource phrases, IReadOnlyList<CrewProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(phrases);
        ArgumentNullException.ThrowIfNull(profiles);
        var errors = new DefinitionErrors();
        Validate(templates, CommsTemplatesFile.RelativePath, errors);
        errors.ThrowIfAny();
        return new CommsContent(templates, phrases, profiles);
    }

    public static void Validate(CommsTemplatesFile file, string label, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(errors);
        if (file.Schema != CommsTemplatesFile.SchemaTag)
            errors.Add(label, "$.schema", $"Expected schema '{CommsTemplatesFile.SchemaTag}' but found '{file.Schema}'.");

        foreach (var key in CommsLineKeys.All)
        {
            if (!file.Lines.ContainsKey(key)) errors.Add(label, $"$.lines.{key}", "Required line is missing.");
        }
        foreach (var (key, text) in file.Lines)
        {
            if (!CommsLineKeys.All.Contains(key, StringComparer.Ordinal)) errors.Add(label, $"$.lines.{key}", "Unknown line key.");
            CheckText(text, label, $"$.lines.{key}", errors);
        }

        if (!file.Reasons.ContainsKey(DefaultReason)) errors.Add(label, "$.reasons", $"A '{DefaultReason}' reason is required.");
        foreach (var (code, text) in file.Reasons) CheckText(text, label, $"$.reasons.{code}", errors);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < file.QuickCommands.Count; i++)
        {
            var command = file.QuickCommands[i];
            if (!ids.Add(command.Id)) errors.Add(label, $"$.quickCommands[{i}].id", $"Duplicate quick command '{command.Id}'.");
            CheckText(command.Label, label, $"$.quickCommands[{i}].label", errors);
        }
    }

    /// <summary>Fills <c>{placeholder}</c>s. Values are inserted literally; <c>{fact}</c> is left for <see cref="FactToken"/>.</summary>
    public static string Fill(string template, params ReadOnlySpan<(string Name, string Value)> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        var text = new StringBuilder(template);
        foreach (var (name, value) in values) text.Replace("{" + name + "}", value);
        return text.ToString();
    }

    public static string FactToken(FactId fact) => $"<FACT:{fact.Value}>";

    public string Line(string key) =>
        _templates.Lines.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"Comms line '{key}' is not defined.");

    /// <summary>The crew's words for a validator reason code, or the default reason.</summary>
    public string Reason(string? reasonCode) =>
        reasonCode is not null && _templates.Reasons.TryGetValue(reasonCode, out var text) ? text : _templates.Reasons[DefaultReason];

    public bool HasReason(string reasonCode) => _templates.Reasons.ContainsKey(reasonCode);

    public IReadOnlyList<CrewOpener> Phrases(CrewId crew, CrewPhraseCategory category) => _phrases.Phrases(crew, category);

    public bool TryGetProfile(CrewId crew, [NotNullWhen(true)] out CrewProfile? profile) => _profiles.TryGetValue(crew, out profile);

    /// <summary>Short name for lines and system messages; the id when the crew member has no profile.</summary>
    public string ShortName(CrewId crew) => _profiles.TryGetValue(crew, out var profile) ? profile.ShortName : crew.Value;

    public bool TryGetQuickCommand(string id, [NotNullWhen(true)] out QuickCommandDefinition? command) => _quickCommands.TryGetValue(id, out command);

    private static void CheckText(string text, string label, string path, DefinitionErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(label, path, "Text is empty.");
            return;
        }
        if (text.Any(char.IsDigit)) errors.Add(label, path, "Text must not contain digits; values enter only through {fact}.");
        if (text.Contains("<FACT", StringComparison.OrdinalIgnoreCase)) errors.Add(label, path, "Use {fact} instead of a raw fact token.");

        for (var open = text.IndexOf('{', StringComparison.Ordinal); open >= 0; open = text.IndexOf('{', open + 1))
        {
            var close = text.IndexOf('}', open + 1);
            var name = close < 0 ? "" : text[(open + 1)..close];
            if (!Placeholders.Contains(name)) errors.Add(label, path, $"Unknown placeholder '{{{name}}}'.");
        }
    }
}
