using Godot;

namespace Usa.Game.UI.Console;

/// <summary>
/// One-time console hints the player has already seen (UI.md 4.4 first-time legend callout), kept in
/// <c>user://console_seen.cfg</c> so they show once per player profile on this machine, not once per mission. Unreadable or
/// missing storage counts as not seen, so a hint is shown again rather than lost.
/// </summary>
internal static class FirstUseFlags
{
    public const string ExternalLegend = "external_legend";

    private const string Path = "user://console_seen.cfg";
    private const string Section = "seen";

    public static bool Seen(string flag)
    {
        var config = new ConfigFile();
        return config.Load(Path) == Error.Ok && config.GetValue(Section, flag, false).AsBool();
    }

    public static void MarkSeen(string flag)
    {
        var config = new ConfigFile();
        // A missing file is fine: the flag starts a new one.
        _ = config.Load(Path);
        config.SetValue(Section, flag, true);
        if (config.Save(Path) != Error.Ok) GD.PushWarning($"[MissionConsole] could not save {Path}");
    }
}
