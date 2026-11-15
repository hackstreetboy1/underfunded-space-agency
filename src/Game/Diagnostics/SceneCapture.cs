using System.Globalization;
using Godot;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Runs another scene windowed and saves a screenshot of it, for review gates. User arguments after <c>--</c>:
/// <c>--scene=res://...tscn</c> (required), <c>--out=res://docs/review/...png</c> (required), <c>--delay=3</c> seconds
/// before the capture (shaders compile and animations settle), <c>--quit=true</c> to exit after saving. <c>--shots=10,30,60</c>
/// saves one screenshot at each of those seconds instead, into <c>--out</c> with <c>{t}</c> replaced by the second, and quits
/// after the last. Prints <c>[Capture]</c> lines; exits non-zero when the scene can't load or an image can't be saved.
/// </summary>
public sealed partial class SceneCapture : Node
{
    private readonly Queue<double> _shots = new();
    private string _output = "";
    private double _remainingS = 3;
    private double _elapsedS;
    private bool _quit = true;
    private bool _done;

    public override void _Ready()
    {
        var args = ParseArgs(OS.GetCmdlineUserArgs());
        _output = args.GetValueOrDefault("out", "");
        if (args.TryGetValue("delay", out var delay) && double.TryParse(delay, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) _remainingS = seconds;
        if (args.TryGetValue("quit", out var quit)) _quit = quit != "false";
        if (args.TryGetValue("shots", out var shots))
        {
            foreach (var shot in shots.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Seconds).Where(s => s >= 0).Order()) _shots.Enqueue(shot);
        }

        var scenePath = args.GetValueOrDefault("scene", "");
        if (scenePath.Length == 0 || _output.Length == 0)
        {
            Finish($"usage: -- --scene=res://x.tscn --out=res://y.png [--delay=3 | --shots=10,30]", 2);
            return;
        }
        if (ResourceLoader.Load<PackedScene>(scenePath) is not { } scene)
        {
            Finish($"cannot load {scenePath}", 1);
            return;
        }

        AddChild(scene.Instantiate());
        var plan = _shots.Count > 0 ? $"shots at {string.Join(", ", _shots)} s" : $"{_remainingS:0.#} s";
        GD.Print($"[Capture] running {scenePath} for {plan}, viewport {GetViewport().GetVisibleRect().Size}");
    }

    public override void _Process(double delta)
    {
        if (_done || _output.Length == 0) return;
        if (_shots.Count > 0)
        {
            _elapsedS += delta;
            if (_elapsedS < _shots.Peek()) return;
            var second = _shots.Dequeue();
            var target = _output.Replace("{t}", second.ToString("000", CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var saved = Save(target, out var message);
            if (!saved) Finish(message, 1);
            else if (_shots.Count == 0) Finish(message, 0);
            else GD.Print($"[Capture] {message}");
            return;
        }

        _remainingS -= delta;
        if (_remainingS > 0) return;
        var ok = Save(_output, out var result);
        Finish(result, ok ? 0 : 1);
    }

    private static bool Save(string output, out string message)
    {
        var image = ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath(output);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An unusable --out path must end the run with an error, not leave the window open forever.
            message = $"cannot create the folder for {path}: {ex.Message}";
            return false;
        }

        var error = image.SavePng(path);
        message = error == Error.Ok
            ? $"saved {image.GetWidth()}x{image.GetHeight()} to {path} (fps {Engine.GetFramesPerSecond():0})"
            : $"saving {path} failed: {error}";
        return error == Error.Ok;
    }

    private void Finish(string message, int exitCode)
    {
        _done = true;
        if (exitCode == 0) GD.Print($"[Capture] {message}");
        else GD.PushError($"[Capture] {message}");
        if (_quit || exitCode != 0) DiagnosticsQuit.Quit(this, exitCode);
    }

    private static double Seconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            var trimmed = arg.TrimStart('-');
            var equals = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0) parsed[trimmed[..equals]] = trimmed[(equals + 1)..];
        }
        return parsed;
    }
}
