using System.Collections.Frozen;
using Godot;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Entry;

/// <summary>
/// The theme type variations of <see cref="ThemeVariations"/> as <see cref="StringName"/>s built once, so a view that
/// re-themes at the UI cadence neither concatenates nor converts a string every update (review 3, R3-14).
/// </summary>
internal static class ThemeVariationNames
{
    private static readonly string[] Tones =
        [ColourTokens.Ok, ColourTokens.Caution, ColourTokens.Critical, ColourTokens.Conflict, ColourTokens.Unknown, ColourTokens.Info, ColourTokens.Accent];

    private static readonly FrozenDictionary<string, StringName> Names = ThemeVariations.All
        .Append(ThemeVariations.Default)
        .Append(ReadoutLargeName)
        .Distinct(StringComparer.Ordinal)
        .ToFrozenDictionary(name => name, name => new StringName(name), StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, StringName> Panels =
        Tones.ToFrozenDictionary(tone => tone, tone => Of(ThemeVariations.BadgePanel(tone)), StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, StringName> Texts =
        Tones.ToFrozenDictionary(tone => tone, tone => Of(ThemeVariations.BadgeText(tone)), StringComparer.Ordinal);

    private const string ReadoutLargeName = "ReadoutLarge";

    public static StringName Badge => Names[ThemeVariations.Badge];

    public static StringName BadgeLabel => Names[ThemeVariations.BadgeLabel];

    public static StringName Small => Names[ThemeVariations.Small];

    public static StringName ReadoutLarge => Names[ReadoutLargeName];

    /// <summary>The cached name; a variation outside <see cref="ThemeVariations.All"/> gets a new one (setup paths only).</summary>
    public static StringName Of(string variation) => Names.TryGetValue(variation, out var name) ? name : new StringName(variation);

    /// <summary><see cref="ThemeVariations.BadgePanel"/> without allocating; neutral for text tones and null.</summary>
    public static StringName BadgePanel(string? colourToken) =>
        colourToken is not null && Panels.TryGetValue(colourToken, out var name) ? name : Badge;

    /// <summary><see cref="ThemeVariations.BadgeText"/> without allocating.</summary>
    public static StringName BadgeText(string? colourToken) =>
        colourToken is not null && Texts.TryGetValue(colourToken, out var name) ? name : BadgeLabel;
}
