using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Content;
using Usa.Game.Diagnostics.Crash;
using Usa.Game.Diagnostics.Overlay;
using Usa.Game.Diagnostics.Repro;
using Usa.Simulation.Data;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Installs the playtest tooling under the composition root: the <see cref="CrashReporter"/> and its player-facing
/// <see cref="CrashNotice"/> in every build and, when
/// <see cref="DebugOverlayGate"/> allows it, the F12 <see cref="DebugOverlay"/>. Both write repros to <see cref="ReproDirectory"/>.
/// </summary>
public static class DebugTools
{
    public const string ReproDirectory = "user://repros";
    private const string Category = "DebugTools";

    public static void Install(Node root, GameServices services, string version)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(services);

        var store = new ReproStore(ProjectSettings.GlobalizePath(ReproDirectory));
        var capture = new GameReproCapture(services, version, store.Time);
        var crash = new CrashReporter { Name = nameof(CrashReporter) };
        crash.Initialize(capture, store, services.Log);
        root.AddChild(crash);

        var notice = new CrashNotice { Name = nameof(CrashNotice) };
        notice.Initialize(crash, services.Router, LoadStrings(services));
        root.AddChild(notice);

        if (!DebugOverlayGate.IsEnabled(out var reason))
        {
            services.Log.Info(Category, $"version {version}; debug overlay off ({reason}); crash repros go to {store.Directory}");
            return;
        }
        var overlay = GD.Load<PackedScene>(DebugOverlay.ScenePath).Instantiate<DebugOverlay>();
        overlay.Name = nameof(DebugOverlay);
        overlay.Initialize(services, capture, store, crash, version);
        root.AddChild(overlay);
        services.Log.Info(Category, $"version {version}; debug overlay on F12 ({reason}); repros go to {store.Directory}");
    }

    // The crash notice must work even when the content files are what broke: it then uses its built-in copy.
    private static StringTable? LoadStrings(GameServices services)
    {
        try
        {
            return StringTable.Load(services.Data);
        }
        catch (Exception ex) when (ex is DefinitionException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            services.Log.Warn(Category, $"crash notice uses its built-in words ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }
}
