using System.Globalization;
using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.Audio.Mixing;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Audio smoke (diagnostics/audio/AudioSmoke.tscn): checks the bus layout GameRoot composed, then plays every
/// <see cref="AudioCue"/> through the shared <see cref="AudioDirector"/> for a moment (loops started and stopped) and the
/// alarm sequencer (raise, acknowledge, clear). For each step it records the bus peak meter (polled once per frame) and
/// the exact peak from a capture tap added at the end of each bus chain (pre-fader), which also catches clicks too
/// short for the meter. Prints <c>[Audio]</c> lines; exits non-zero on a layout problem or a cue that produced no signal.
/// It quits while loops are still playing, which exercises the director's shutdown path.
/// </summary>
public sealed partial class AudioSmokeTest : Node
{
    private const double CueSeconds = 0.45;
    private const float SilentDb = -120f;
    private const double LoopsBeforeQuitSeconds = 0.25;
    private const double ThrusterSpacingSeconds = 0.05;

    private readonly List<string> _failures = [];
    private readonly Queue<(string Name, AudioBus Bus, Action Start, Action? Stop)> _steps = new();
    private readonly Dictionary<AudioBus, AudioEffectCapture> _taps = [];
    private AudioDirector? _audio;
    private (string Name, AudioBus Bus, Action Start, Action? Stop)? _current;
    private double _elapsedS;
    private float _meterDb;
    private float _tapPeak;
    private int _played;

    public override void _Ready()
    {
        if (GetNodeOrNull<GameRoot>("/root/GameRoot") is not { IsComposed: true } root)
        {
            Fail("GameRoot autoload missing or not composed");
            Finish();
            return;
        }

        var audio = root.Services.Audio;
        _audio = audio;
        GD.Print($"[Audio] driver {AudioServer.GetDriverName()}, mix rate {AudioServer.GetMixRate():0} Hz, output latency {AudioServer.GetOutputLatency() * 1000:0} ms");
        CheckLayout();
        AddTaps();

        foreach (var spec in CueTable.All)
        {
            var cue = spec.Cue;
            _steps.Enqueue(spec.IsLoop
                ? ($"{cue} (loop)", spec.Bus, () => audio.StartLoop(cue), () => audio.StopLoop(cue))
                : (cue.ToString(), spec.Bus, () => audio.Play(cue), null));
        }
        foreach (var priority in Enum.GetValues<AlarmPriority>())
        {
            var id = $"smoke_{priority}".ToLowerInvariant();
            _steps.Enqueue(($"RaiseAlarm {priority} + ack", AudioBus.Alarms, () => audio.RaiseAlarm(id, priority), () =>
            {
                audio.AcknowledgeAlarms();
                audio.ClearAlarm(id);
            }));
        }

        // Worst realistic load on the Alarms bus: its three voices busy with the three loudest alarm sounds started together
        // (the sequencer never stacks them, but direct cues can), read pre-fader by the tap. Earlier steps' tails would add
        // to it, so it starts from silence.
        _steps.Enqueue(("Alarms stacked (3 voices)", AudioBus.Alarms, () =>
        {
            audio.StopAll();
            audio.StartLoop(AudioCue.AlarmMaster);
            audio.Play(AudioCue.AlarmWarning);
            audio.Play(AudioCue.AlarmCaution);
        }, () => audio.StopLoop(AudioCue.AlarmMaster)));

        // The same for the SFX bus: all eight voices busy (three loops, both impacts, three thruster pulses). The pulses'
        // anti-retrigger gap keeps them 40 ms apart, so they are spaced just over it instead of starting together.
        _steps.Enqueue(("SFX stacked (8 voices)", AudioBus.Sfx, () =>
        {
            audio.StopAll();
            audio.StartLoop(AudioCue.RetroBurn);
            audio.StartLoop(AudioCue.EntryRoar);
            audio.StartLoop(AudioCue.StuckThruster);
            audio.Play(AudioCue.ChuteDeploy);
            audio.Play(AudioCue.Splashdown);
            audio.Play(AudioCue.ThrusterPulse);
            for (var i = 1; i <= 2; i++) GetTree().CreateTimer(ThrusterSpacingSeconds * i).Timeout += () => audio.Play(AudioCue.ThrusterPulse);
        }, () =>
        {
            audio.StopLoop(AudioCue.RetroBurn);
            audio.StopLoop(AudioCue.EntryRoar);
            audio.StopLoop(AudioCue.StuckThruster);
        }));
    }

    public override void _Process(double delta)
    {
        if (_audio is null) return;
        if (_current is { } step)
        {
            _elapsedS += delta;
            var bus = AudioBusSetup.IndexOf(step.Bus);
            _meterDb = Math.Max(_meterDb, Math.Max(AudioServer.GetBusPeakVolumeLeftDb(bus, 0), AudioServer.GetBusPeakVolumeRightDb(bus, 0)));
            _tapPeak = Math.Max(_tapPeak, DrainPeak(_taps[step.Bus]));
            if (_elapsedS < CueSeconds) return;

            step.Stop?.Invoke();
            var tapDb = _tapPeak > 0 ? 20f * MathF.Log10(_tapPeak) : float.NegativeInfinity;
            if (!(tapDb > SilentDb)) Fail($"{step.Name} produced no signal on {AudioBuses.Name(step.Bus)}");
            GD.Print($"[Audio] {step.Name,-28} bus {AudioBuses.Name(step.Bus),-8} tap peak {Db(tapDb),-14} meter {Db(_meterDb)}");
            _current = null;
        }

        if (_steps.Count == 0)
        {
            Summarize();
            return;
        }

        var next = _steps.Dequeue();
        foreach (var tap in _taps.Values) tap.ClearBuffer();
        _current = next;
        _elapsedS = 0;
        _meterDb = float.NegativeInfinity;
        _tapPeak = 0;
        _played++;
        next.Start();
    }

    private void CheckLayout()
    {
        var names = new List<string>();
        for (var i = 0; i < AudioServer.BusCount; i++)
        {
            var name = AudioServer.GetBusName(i);
            var effects = Enumerable.Range(0, AudioServer.GetBusEffectCount(i)).Select(e => AudioServer.GetBusEffect(i, e).GetType().Name);
            GD.Print($"[Audio] bus {i} {name,-12} -> {(i == 0 ? "(out)" : AudioServer.GetBusSend(i).ToString()),-8} {AudioServer.GetBusVolumeDb(i),6:0.0} dB{(AudioServer.IsBusMute(i) ? " muted" : "")} [{string.Join(", ", effects)}]");
            if (names.Contains(name)) Fail($"duplicate bus {name}");
            names.Add(name);
        }

        foreach (var bus in AudioBuses.All)
        {
            var index = AudioBusSetup.IndexOf(bus);
            if (index < 0)
            {
                Fail($"bus {AudioBuses.Name(bus)} missing");
                continue;
            }
            if (AudioServer.GetBusSend(index) != AudioBuses.MasterName) Fail($"bus {AudioBuses.Name(bus)} sends to {AudioServer.GetBusSend(index)}, not Master");
        }

        RequireEffect<AudioEffectHardLimiter>(AudioBuses.MasterName);
        RequireEffect<AudioEffectAmplify>(AudioBuses.Name(AudioBus.Ambience));
        RequireEffect<AudioEffectHighPassFilter>(AudioBuses.Name(AudioBus.Speech));
        RequireEffect<AudioEffectLowPassFilter>(AudioBuses.Name(AudioBus.Speech));
        RequireEffect<AudioEffectCompressor>(AudioBuses.Name(AudioBus.Speech));
        var mic = AudioServer.GetBusIndex("Mic");
        if (mic < 0 || !AudioServer.IsBusMute(mic)) Fail("Mic capture bus missing or audible");
    }

    private void RequireEffect<T>(string busName)
        where T : AudioEffect
    {
        var bus = AudioServer.GetBusIndex(busName);
        if (bus < 0) return;
        for (var i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is T) return;
        }
        Fail($"bus {busName} lacks {typeof(T).Name}");
    }

    private void AddTaps()
    {
        foreach (var bus in AudioBuses.All)
        {
            var index = AudioBusSetup.IndexOf(bus);
            if (index < 0) continue;
            var tap = new AudioEffectCapture { BufferLength = 1f };
            AudioServer.AddBusEffect(index, tap);
            _taps[bus] = tap;
        }
    }

    private static float DrainPeak(AudioEffectCapture tap)
    {
        var frames = tap.GetFramesAvailable();
        if (frames == 0) return 0;
        var peak = 0f;
        foreach (var frame in tap.GetBuffer(frames)) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
        return peak;
    }

    private void Summarize()
    {
        GD.Print($"[Audio] {_played} steps played");

        // Quit with voices still sounding, the way the game should: through the director's release gate (DiagnosticsQuit).
        // A clean exit (no crash, no leaked AudioStreamGeneratorPlayback; tools/check.ps1 fails on those) covers shutdown.
        var audio = _audio!;
        _audio = null;
        audio.StartLoop(AudioCue.CabinAmbience);
        audio.StartLoop(AudioCue.AlarmMaster);
        audio.Play(AudioCue.AlarmWarning);
        GetTree().CreateTimer(LoopsBeforeQuitSeconds).Timeout += Finish;
    }

    private void Fail(string message)
    {
        _failures.Add(message);
        GD.PushError($"[Audio] {message}");
    }

    private void Finish()
    {
        GD.Print(_failures.Count == 0 ? "[Audio] PASS" : $"[Audio] FAIL ({_failures.Count})");
        DiagnosticsQuit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private static string Db(float db) => float.IsFinite(db) && db > -199f ? $"{db.ToString("0.0", CultureInfo.InvariantCulture)} dBFS" : "silent";
}
