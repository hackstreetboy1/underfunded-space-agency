using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Status;

/// <summary>
/// Rendering of a knowledge state (UI.md 2.3). The badge text itself (with the stale age) comes from
/// <see cref="ReadoutText.Badge"/>; this adds the icon and colours.
/// </summary>
/// <param name="BadgeColourToken">Badge colour; null when there is no badge (known and fresh).</param>
/// <param name="ValueColourToken">Colour of the value text.</param>
/// <param name="DashedOutline">Draw the readout frame dashed (unknown).</param>
public sealed record KnowledgeVisual(
    ReadoutKnowledge State,
    StatusShape Icon,
    string Glyph,
    string? BadgeColourToken,
    string ValueColourToken,
    bool DashedOutline);
