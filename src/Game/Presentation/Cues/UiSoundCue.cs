using Usa.Game.Presentation.Audio.Mixing;

namespace Usa.Game.Presentation.Cues;

/// <summary>
/// Which UI sound a player interaction makes (GDD 14 "UI ticks"). Controls tick by default; a scene can tag a control, or
/// any ancestor of it, with the <see cref="MetaKey"/> metadata to change that: <c>none</c>, <c>tick</c>, <c>confirm</c> or
/// <c>error</c>. The nearest tag wins. Sounds follow the player's own input only, never simulation state.
/// </summary>
public static class UiSoundCue
{
    /// <summary>Godot metadata key read from the control and its ancestors.</summary>
    public const string MetaKey = "usa_ui_sound";

    public const string None = "none";
    public const string Tick = "tick";
    public const string Confirm = "confirm";
    public const string Error = "error";

    /// <summary>
    /// The cue for an interaction, given the tags from the control outwards (null entries are untagged levels). Untagged or
    /// unknown tags fall through to the next level; nothing tagged means <see cref="AudioCue.UiTick"/>.
    /// </summary>
    /// <returns>The cue to play, or null when the nearest tag is <c>none</c>.</returns>
    public static AudioCue? Resolve(IEnumerable<string?> tagsFromControlOutwards)
    {
        ArgumentNullException.ThrowIfNull(tagsFromControlOutwards);
        foreach (var tag in tagsFromControlOutwards)
        {
            if (TryParse(tag, out var cue)) return cue;
        }

        return AudioCue.UiTick;
    }

    /// <summary>Parses one tag (case-insensitive, trimmed).</summary>
    /// <returns>False for null, empty or unknown tags; true with a null cue for <c>none</c>.</returns>
    public static bool TryParse(string? tag, out AudioCue? cue)
    {
        cue = null;
        var trimmed = tag?.Trim();
        if (Is(trimmed, None)) return true;
        if (Is(trimmed, Tick)) cue = AudioCue.UiTick;
        else if (Is(trimmed, Confirm)) cue = AudioCue.UiConfirm;
        else if (Is(trimmed, Error)) cue = AudioCue.UiError;
        return cue is not null;
    }

    private static bool Is(string? tag, string name) => string.Equals(tag, name, StringComparison.OrdinalIgnoreCase);
}
