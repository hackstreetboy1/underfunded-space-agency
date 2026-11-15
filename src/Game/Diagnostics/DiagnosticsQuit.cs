using Godot;
using Usa.Game.Bootstrap;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Quits a diagnostics run the way the game should quit: through the audio director's release gate when the app is
/// composed, so voices still sounding do not end up as leaked generator playbacks at exit.
/// </summary>
public static class DiagnosticsQuit
{
    public static void Quit(Node from, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(from);
        var tree = from.GetTree();
        if (from.GetNodeOrNull<GameRoot>("/root/GameRoot") is { IsComposed: true } root)
            root.Services.Audio.ReleaseVoicesThen(() => tree.Quit(exitCode));
        else
            tree.Quit(exitCode);
    }
}
