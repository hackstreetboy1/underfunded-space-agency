using Godot;

namespace Usa.Game.UI.Entry;

/// <summary>
/// Remembers the variation each control was given, so re-applying the same one is a dictionary hit: reading
/// <c>Control.ThemeTypeVariation</c> back would allocate a <see cref="StringName"/> wrapper every call (R3-14).
/// One per view; names come from <see cref="ThemeVariationNames"/>, so equal variations are the same instance.
/// </summary>
internal sealed class VariationSetter
{
    private readonly Dictionary<Control, StringName> _current = new(ReferenceEqualityComparer.Instance);

    public void Set(Control control, StringName variation)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(variation);
        if (_current.TryGetValue(control, out var current) && ReferenceEquals(current, variation)) return;
        _current[control] = variation;
        control.ThemeTypeVariation = variation;
    }

    /// <summary>A badge panel and its word in one tone.</summary>
    public void SetBadge(Control panel, Control word, string? colourToken)
    {
        Set(panel, ThemeVariationNames.BadgePanel(colourToken));
        Set(word, ThemeVariationNames.BadgeText(colourToken));
    }
}
