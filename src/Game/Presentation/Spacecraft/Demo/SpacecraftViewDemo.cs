using System.Globalization;
using Godot;

namespace Usa.Game.Presentation.Spacecraft.Demo;

/// <summary>
/// Driver of <c>res://diagnostics/spacecraft/SpacecraftViewDemo.tscn</c>: runs <see cref="SpacecraftView"/> inside a
/// SubViewport (as the External tab will) against <see cref="DemoSpacecraftSource"/>, and prints what the view
/// reports. Keys: R reset camera, G focus last revealed, V focus Lodestar, F focus Tandem, 1 jettison the adapter, 2 the retro section (the view keeps them dropped until re-initialized), 3 entry heating, 4 drogue, 5 main chute,
/// 6 Lodestar power, 7 reduced motion, 8 reveal the next part (outline flash), 9 fast orbit (Sun glow sets and rises),
/// 0 splashdown (the sea view with the recovery forces closing in over 30 s) and back to orbit, T turn Tandem end over end (it must still hang nose-up under a chute), E entry from 120 km to drogue deploy (heat shield first), L descend under the main chute to splashdown, D play the docking approach
/// (<c>--demo-approach-sec=8</c> sets its length).
/// <para>
/// Scripted shots (review captures through <c>diagnostics/capture/Capture.tscn</c>), user arguments after <c>--</c>:
/// <c>--demo-camera=yaw,pitch[,distance][@seconds]</c> (degrees, metres; after a focus key, give it a later time),
/// <c>--demo-orbit-deg=165</c> (start angle of the orbit; the Sun sets behind the Earth at about 175°),
/// <c>--demo-keys=9@0.2,8@3</c> (key presses at seconds since start, same keys as above),
/// <c>--demo-reveal-all=true</c> (every revealable part confirmed nominal from the start, to judge the palette look),
/// <c>--demo-hud=false</c> (hide the text overlay).
/// </para>
/// </summary>
public sealed partial class SpacecraftViewDemo : Control
{
    private static readonly string[] LightNames = ["ACS", "LATCH", "ENG ARM", "PWR"];

    private readonly List<string> _events = [];
    private readonly List<(double AtSec, Action Run)> _scripted = [];
    private readonly DemoSpacecraftSource _source = new();
    private SpacecraftView _view = null!;
    private Label _info = null!;
    private double _elapsedSec;
    private double _approachSec = 8;
    private string _lastApproach = "";

    public override void _Ready()
    {
        _view = GetNode<SpacecraftView>("%SpacecraftView");
        _info = GetNode<Label>("%Info");
        _view.Initialize(_source);
        _view.ComponentPicked += id => Log($"picked {id}");
        _view.PickCleared += () => Log("pick cleared");
        _view.ComponentRevealed += id => Log($"revealed {id} (outline flash)");
        _view.SectionJettisoned += (vehicle, section) => Log($"{vehicle} jettisoned {section}");
        _view.EntryHudVisibilityChanged += visible => Log($"entry HUD {(visible ? "shown" : "hidden")}");
        ApplyScriptArgs(OS.GetCmdlineUserArgs());
    }

    public override void _Process(double delta)
    {
        _elapsedSec += delta;
        for (var i = 0; i < _scripted.Count; i++)
        {
            if (_scripted[i].AtSec > _elapsedSec) continue;
            var run = _scripted[i].Run;
            _scripted.RemoveAt(i--);
            run();
        }

        _source.Advance(delta);
        var approach = Approach();
        if (approach != _lastApproach)
        {
            _lastApproach = approach;
            if (approach.Length > 0) GD.Print($"[Demo] t={_elapsedSec:0.00}{approach}");
        }
        var anchor = _view.TryGetTagAnchor("tandem/nose/docking_bar", out var screen)
            ? string.Create(CultureInfo.InvariantCulture, $"({screen.X:0}, {screen.Y:0})")
            : "off screen";
        _info.Text = string.Create(CultureInfo.InvariantCulture,
            $"""
            Spacecraft view demo   t = {_source.LoopTimeSec:0.0} s{EntryText()}{Approach()} / {DemoSpacecraftSource.LoopSec:0} s   roll {_source.RollRateDegPerSec:0.0} °/s   {(_source.Docked ? "docked" : "free")}   reduced motion {(_view.ReducedMotion ? "on" : "off")}
            panel glance at {DemoSpacecraftSource.PanelGlanceAtSec:0} s, reveal T8 at {DemoSpacecraftSource.RevealAtSec:0} s, undock at {DemoSpacecraftSource.UndockAtSec:0} s   docking-bar STALE anchor {anchor}
            Agena lights: {LightSummary()}
            drag orbit · wheel zoom · click pick · R reset · G focus last revealed · V Agena · 1/2 jettison · 3 heating · 4/5 chutes · 6 Agena power · 7 reduced motion · 8 reveal next · 9 fast orbit · 0 splashdown · E entry · L descent · D docking approach
            {string.Join("\n", _events)}
            """);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (Press(key.Keycode)) GetViewport().SetInputAsHandled();
    }

    private bool Press(Key key)
    {
        switch (key)
        {
            case Key.R: _view.ResetCamera(); break;
            case Key.G: Log(_view.FocusLastRevealed() ? $"focus {_view.LastRevealedComponentId}" : "nothing revealed yet"); break;
            case Key.V: _view.FocusVehicle("lodestar"); break;
            case Key.F: _view.FocusVehicle("tandem"); break;
            case Key.Key1: _source.ToggleJettison("adapter"); break;
            case Key.Key2: _source.ToggleJettison("retro"); break;
            case Key.Key3: _source.ToggleHeating(); break;
            case Key.Key4: Log($"drogue {_source.CycleChute("tandem/chute/drogue")}"); break;
            case Key.Key5: Log($"main {_source.CycleChute("tandem/chute/main")}"); break;
            case Key.Key6: Log($"Agena power {(_source.ToggleLodestarPower() ? "on" : "off")}"); break;
            case Key.Key7: _view.ReducedMotion = !_view.ReducedMotion; break;
            case Key.Key8: Log(_source.RevealNext() is { } id ? $"crew confirms {id}" : "every demo part already revealed"); break;
            case Key.Key9: Log($"orbit {(_source.ToggleFastOrbit() ? "fast (60 s)" : "real (90 min)")}"); break;
            case Key.Key0: Log(_source.ToggleSplashdown() ? "splashdown" : "back in orbit"); break;
            case Key.T: Log(_source.ToggleNoseDown() ? "Tandem flipped end over end" : "Tandem upright"); break;
            case Key.E:
                _source.StartEntry();
                Log("entry from the 120 km interface");
                break;
            case Key.L:
                _source.StartDescent();
                Log("descent under the chutes");
                break;
            case Key.D:
                _source.StartDockingApproach(_approachSec);
                // Start from the separated pair as the session would show it (no undocking departure from the teleport).
                _view.Initialize(_source);
                Log($"docking approach over {_approachSec:0.#} s");
                break;
            default: return false;
        }
        return true;
    }

    private void ApplyScriptArgs(string[] args)
    {
        foreach (var arg in args)
        {
            var equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0) continue;
            var name = arg[..equals].TrimStart('-');
            var value = arg[(equals + 1)..];
            switch (name)
            {
                case "demo-camera":
                    var timed = value.Split('@');
                    var parts = timed[0].Split(',');
                    var cameraAtSec = timed.Length > 1 && TryParse(timed[1], out var cameraSec) ? cameraSec : 0.0;
                    if (parts.Length >= 2 && TryParse(parts[0], out var yaw) && TryParse(parts[1], out var pitch))
                    {
                        float? distance = parts.Length > 2 && TryParse(parts[2], out var metres) ? (float)metres : null;
                        _scripted.Add((cameraAtSec, () => _view.OrbitCamera((float)yaw, (float)pitch, distance)));
                    }
                    break;
                case "demo-reveal-all" when value != "false":
                    foreach (var id in _view.Presenters.Keys) _source.ConfirmNominal(id);
                    break;
                case "demo-orbit-deg":
                    if (TryParse(value, out var orbitDeg)) _source.SetOrbitAngleDeg(orbitDeg);
                    break;
                case "demo-keys":
                    foreach (var press in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var at = press.IndexOf('@', StringComparison.Ordinal);
                        if (at != 1 || !TryParse(press[(at + 1)..], out var atSec)) continue;
                        var key = (Key)char.ToUpperInvariant(press[0]);
                        _scripted.Add((atSec, () => Press(key)));
                    }
                    break;
                case "demo-approach-sec":
                    if (TryParse(value, out var approachSec)) _approachSec = approachSec;
                    break;
                case "demo-hud":
                    _info.Visible = value != "false";
                    break;
            }
        }
    }

    private string Approach()
    {
        if (_source.DockingApproach01 is not { } progress) return "";
        var firing = new List<string>();
        if (_view.Approach is not null && _view.GetNodeOrNull<VehicleRig>("Vehicles/TandemRig")?.Thrusters is { } jets)
        {
            for (var i = 0; i < jets.PlumeCount; i++)
            {
                if (jets.CommandedAt(i) > 0) firing.Add(jets.PlumeComponentIdAt(i).Split('/')[^1]);
            }
        }
        return string.Create(CultureInfo.InvariantCulture, $"   docking {progress:P0} firing [{string.Join(" ", firing)}]");
    }

    private string EntryText() => _source.EntryTimeSec is { } entrySec
        ? string.Create(CultureInfo.InvariantCulture, $"   entry {entrySec:0.0} s  alt {_source.Backdrop.AltitudeKm:0} km")
        : "";

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private string LightSummary()
    {
        var lights = _view.StatusLights;
        if (!lights.IsBound) return "not bound";
        var parts = new string[LightNames.Length];
        for (var i = 0; i < parts.Length; i++) parts[i] = $"{LightNames[i]} {lights.GetState(i)}";
        return string.Join(" · ", parts);
    }

    private void Log(string message)
    {
        _events.Add(string.Create(CultureInfo.InvariantCulture, $"[{_source.LoopTimeSec:00.0}] {message}"));
        if (_events.Count > 6) _events.RemoveAt(0);
    }
}
