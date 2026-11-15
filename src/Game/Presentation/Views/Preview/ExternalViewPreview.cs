using System.Globalization;
using Godot;
using Usa.Game.Presentation.Spacecraft.Demo;

namespace Usa.Game.Presentation.Views.Preview;

/// <summary>
/// Driver of <c>res://scenes/views/ExternalViewPreview.tscn</c>: <see cref="ExternalView"/> filling the window against
/// <see cref="DemoSpacecraftSource"/>. Keys: R reset camera, F focus Tandem, V focus Lodestar, G focus last revealed,
/// 8 reveal the next part, 1/2 jettison adapter/retro, 3 entry heating, 4/5 drogue/main, 0 splashdown and back.
/// User argument after <c>--</c>: <c>--preview-keys=0@1,8@3</c> presses keys at seconds since start (review captures).
/// </summary>
public sealed partial class ExternalViewPreview : Control
{
    private readonly DemoSpacecraftSource _source = new();
    private readonly List<(double AtSec, Key Key)> _scripted = [];
    private readonly List<string> _events = [];
    private ExternalView _view = null!;
    private Label _info = null!;
    private double _elapsedSec;

    public override void _Ready()
    {
        _view = GetNode<ExternalView>("%ExternalView");
        _info = GetNode<Label>("%Info");
        _view.Initialize(_source);
        _view.View.ComponentPicked += id => Log($"picked {id}");
        _view.View.ComponentRevealed += id => Log($"revealed {id}");
        _view.View.SeaViewChanged += atSea => Log(atSea ? "sea view" : "orbit view");
        _view.View.SectionJettisoned += (vehicle, section) => Log($"{vehicle} jettisoned {section}");
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            const string prefix = "--preview-keys=";
            if (!arg.StartsWith(prefix, StringComparison.Ordinal)) continue;
            foreach (var press in arg[prefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = press.IndexOf('@', StringComparison.Ordinal);
                if (at == 1 && double.TryParse(press[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var atSec))
                {
                    _scripted.Add((atSec, (Key)char.ToUpperInvariant(press[0])));
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        _elapsedSec += delta;
        for (var i = _scripted.Count - 1; i >= 0; i--)
        {
            if (_scripted[i].AtSec > _elapsedSec) continue;
            var key = _scripted[i].Key;
            _scripted.RemoveAt(i);
            Press(key);
        }
        _source.Advance(delta);
        _info.Text = string.Create(CultureInfo.InvariantCulture,
            $"""
            External view preview   t = {_source.LoopTimeSec:0.0} s   {(_view.View.IsAtSea ? $"at sea, recovery {_source.RecoveryProgress01:0.00}" : "orbit")}
            drag orbit · wheel zoom · click pick · R reset · F/V focus Gemini 8/Agena · G last revealed · 8 reveal · 1/2 jettison · 3 heating · 4/5 chutes · 0 splashdown
            {string.Join("\n", _events)}
            """);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && Press(key.Keycode)) GetViewport().SetInputAsHandled();
    }

    private bool Press(Key key)
    {
        switch (key)
        {
            case Key.R: _view.ResetCamera(); break;
            case Key.F: _view.FocusVehicle("tandem"); break;
            case Key.V: _view.FocusVehicle("lodestar"); break;
            case Key.G: Log(_view.FocusLastRevealed() ? "focus last revealed" : "nothing revealed yet"); break;
            case Key.Key8: Log(_source.RevealNext() is { } id ? $"crew confirms {id}" : "all demo parts revealed"); break;
            case Key.Key1: _source.ToggleJettison("adapter"); break;
            case Key.Key2: _source.ToggleJettison("retro"); break;
            case Key.Key3: _source.ToggleHeating(); break;
            case Key.Key4: Log($"drogue {_source.CycleChute("tandem/chute/drogue")}"); break;
            case Key.Key5: Log($"main {_source.CycleChute("tandem/chute/main")}"); break;
            case Key.Key0: Log(_source.ToggleSplashdown() ? "splashdown" : "back in orbit"); break;
            default: return false;
        }
        return true;
    }

    private void Log(string message)
    {
        _events.Add(string.Create(CultureInfo.InvariantCulture, $"[{_elapsedSec:00.0}] {message}"));
        if (_events.Count > 5) _events.RemoveAt(0);
    }
}
