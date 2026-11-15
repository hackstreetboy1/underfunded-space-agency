using System.Runtime.InteropServices;
using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Diagnostics.Overlay;
using Usa.Game.Diagnostics.Repro;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Settings;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Captures a <see cref="ReproDocument"/> of the app as it is now: the running session (seed, run tag, command log, hash
/// sequence), the host's pacing and freeze reasons, backend health and the settings that change play. Main thread only
/// (it reads the session and Godot state), except as a last resort from a dying process.
/// </summary>
public sealed class GameReproCapture
{
    private readonly GameServices _services;
    private readonly TimeProvider _time;

    public GameReproCapture(GameServices services, string version, TimeProvider? time = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _time = time ?? TimeProvider.System;
        App = new ReproApp(
            ProjectSettings.GetSetting("application/config/name", "Underfunded Space Agency").AsString(),
            version,
            GameRoot.BuildPhase,
            Engine.GetVersionInfo()["string"].AsString(),
            OS.IsDebugBuild(),
            OS.HasFeature("editor"),
            $"{OS.GetName()} {OS.GetVersion()}",
            RuntimeInformation.FrameworkDescription);
    }

    public ReproApp App { get; }

    public ReproDocument Capture(string reason, ReproFailure? failure = null, string? note = null)
    {
        var runtime = _services.Flow.Runtime;
        if (failure is not null)
            failure = failure with { Message = LogRedactor.Redact(failure.Message), Detail = failure.Detail is { } detail ? LogRedactor.Redact(detail) : null };
        var context = new ReproContext(_time.GetUtcNow(), reason, App, Settings(), Host(), failure, note);
        return ReproBuilder.Build(context, runtime?.Mission.Session, runtime?.Launch.GuidedFirstRun ?? false, runtime?.Launch.Resume is not null);
    }

    private ReproSettings Settings()
    {
        var current = _services.Settings.Current;
        return new ReproSettings(
            current.Gameplay.TutorialCallouts,
            current.Voice.MicEnabled,
            current.Display.ReducedMotion,
            current.Display.HighContrast,
            current.Display.UiScale,
            string.Equals(current.BackendUrl, GameSettings.DefaultBackendUrl, StringComparison.OrdinalIgnoreCase));
    }

    private ReproHost Host()
    {
        var host = _services.Flow.Host;
        var running = host is not null && host.IsRunning;
        return new ReproHost(
            _services.Router.Current.ToString(),
            running ? host!.CompressionRate : 0,
            running ? host!.CompressionReason.ToString() : "no session",
            running ? host!.EffectiveRate : 0,
            _services.Flow.Runtime?.Mission.Session.FreezeReasons.ToString() ?? "no session",
            _services.Freeze.Reasons.Value.ToString(),
            BackendStatusSource.Describe(_services));
    }
}
