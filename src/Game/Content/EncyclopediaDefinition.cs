using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>Root of <c>catalog/encyclopedia.json</c> (schema <c>encyclopedia/1</c>).</summary>
/// <param name="EraLabels">Display label per era tag, e.g. <c>modern reference</c>.</param>
/// <param name="Categories">Display label per category id.</param>
public sealed record EncyclopediaDefinition(
    string Schema,
    IReadOnlyDictionary<string, string> EraLabels,
    IReadOnlyDictionary<string, string> Categories,
    IReadOnlyList<EncyclopediaEntry> Entries) : IVersionedDefinition
{
    public const string SchemaTag = "encyclopedia/1";
    public const string RelativePath = "catalog/encyclopedia.json";
}

/// <param name="Term">The word as it appears in the UI; tooltips match it (and each part of "A / B" or "A, B").</param>
/// <param name="FirstUse">One-sentence definition shown the first time the term appears.</param>
/// <param name="Related">Ids of other entries.</param>
/// <param name="ImageKey">
/// The approved curated image (Gate 6 NASA package) as its <c>content/nasa/manifest.json</c> key, <c>&lt;source&gt;:&lt;itemId&gt;</c>
/// (e.g. <c>image_library:s64-03506</c>); null when the entry has none.
/// </param>
public sealed record EncyclopediaEntry(
    string Id,
    string Title,
    string Term,
    string Category,
    string Era,
    string FirstUse,
    string Body,
    IReadOnlyList<string> Related,
    IReadOnlyList<EncyclopediaSource> Sources,
    string? ImageKey = null);

public sealed record EncyclopediaSource(
    string Title,
    string Publisher,
    EncyclopediaSourceKind Kind,
    string? Author = null,
    Uri? Url = null);

public enum EncyclopediaSourceKind
{
    Textbook,
    Report,
    Web,
}

/// <summary>A tooltip definition (UI.md 6.3: "Periapsis — the lowest point of your orbit").</summary>
public sealed record FirstUseDefinition(string EntryId, string Term, string Text);
