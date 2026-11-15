using System.Diagnostics.CodeAnalysis;
using Godot;
using Usa.Game.Presentation.Audio.Dsp;
using Usa.Game.Presentation.Audio.Mixing;

namespace Usa.Game.Presentation.Audio;

/// <summary>
/// Single entry point for game audio (SCRIPTING.md 12.5). Maps cues to procedural sounds on their
/// buses with priorities and voice limits, ducks Ambience under Speech, and runs the alarm
/// cadence (UI.md 3.10). It knows nothing about game state: the host calls <see cref="Play"/>,
/// <see cref="StartLoop"/>, <see cref="RaiseAlarm"/>, <see cref="AcknowledgeAlarms"/> and friends, and
/// <see cref="SetFrozen"/> from the app's freeze signal. Every sound is pre-rendered at <c>_Ready</c>
/// (<see cref="SoundFactory.Prewarm"/>), so playing never synthesizes on the main thread.
/// </summary>
public sealed partial class AudioDirector : Node
{
    // Several mix passes and frames: enough for the server to fade out and drop stopped playbacks.
    private const double VoiceReleaseSeconds = 0.1;

    private readonly MixProfile _mix = MixProfile.Default;
    private readonly SoundFactory _factory = new();
    private readonly Dictionary<AudioBus, AudioSynthPlayer[]> _pools = [];
    private readonly Dictionary<AudioBus, StringName> _busNames = [];
    private readonly Dictionary<AudioBus, float> _playerVolumes = [];
    private readonly Dictionary<AudioCue, (AudioSynthPlayer Player, ISoundGenerator Generator)> _loops = [];
    private readonly Dictionary<AudioCue, double> _lastTriggerSeconds = [];
    private readonly List<AlarmAction> _alarmActions = [];
    private readonly AlarmSequencer _alarms = new(MixProfile.Default.Alarms);
    private readonly Ducker _ducker = new(MixProfile.Default.Ducking);
    private AudioEffectAmplify? _duckStage;
    private int _speechBusIndex = -1;
    private long _sequence;
    private uint _seed = 1;
    private double _clockSeconds;
    private bool _frozen;

    /// <summary>True while the game is frozen: alarm cadence and non-UI voices hold (see <see cref="SetFrozen"/>).</summary>
    public bool IsFrozen => _frozen;

    /// <summary>Loop cues started and not stopped (fading-out loops excluded); diagnostics check none outlive a mission.</summary>
    public IReadOnlyList<AudioCue> RunningLoops()
    {
        var running = new List<AudioCue>();
        foreach (var cue in _loops.Keys.ToList())
        {
            if (TryGetLoop(cue, out _)) running.Add(cue);
        }

        return running;
    }

    public override void _EnterTree()
    {
        _duckStage = AudioBusSetup.EnsureBuses(_mix);
        _speechBusIndex = AudioBusSetup.IndexOf(AudioBus.Speech);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        foreach (var bus in _mix.Buses)
        {
            var name = AudioBuses.Name(bus.Bus);
            _busNames[bus.Bus] = name;
            var pool = new AudioSynthPlayer[bus.Voices];
            for (var i = 0; i < pool.Length; i++)
            {
                pool[i] = new AudioSynthPlayer { Name = $"{name}Voice{i}" };
                AddChild(pool[i]);
            }

            _pools[bus.Bus] = pool;
            SetBusVolume(bus.Bus, 1f);
        }

        _factory.Prewarm();
    }

    public override void _ExitTree()
    {
        StopAll();
        _duckStage?.Dispose();
        _duckStage = null;
    }

    // Closing the window quits at the end of this frame; stopping now gives the audio server its best chance to drop
    // the playbacks before shutdown (see ReleaseVoicesThen).
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) StopAll();
    }

    /// <summary>
    /// Stops every voice and calls <paramref name="released"/> once the audio server has dropped their playbacks; quit
    /// from there. A stopped playback stays in the server's mix list until a mix pass and a main-loop frame have run, and
    /// whatever is still listed when the engine shuts down is reported as a leaked <c>AudioStreamGeneratorPlayback</c>.
    /// </summary>
    public void ReleaseVoicesThen(Action released)
    {
        ArgumentNullException.ThrowIfNull(released);
        StopAll();
        GetTree().CreateTimer(VoiceReleaseSeconds, processAlways: true, ignoreTimeScale: true).Timeout += released;
    }

    public override void _Process(double delta)
    {
        if (!_frozen)
        {
            _clockSeconds += delta;
            AdvanceAlarms(delta);
        }

        if (_duckStage is not null && _speechBusIndex >= 0)
        {
            var speechPeakDb = Math.Max(
                AudioServer.GetBusPeakVolumeLeftDb(_speechBusIndex, 0),
                AudioServer.GetBusPeakVolumeRightDb(_speechBusIndex, 0));
            _duckStage.VolumeDb = _ducker.Update(speechPeakDb, delta);
        }
    }

    /// <summary>Plays a one-shot cue (a loop cue is started instead). Intensity scales its level.</summary>
    public void Play(AudioCue cue, float intensity = 1f)
    {
        var spec = CueTable.Get(cue);
        if (spec.IsLoop)
        {
            StartLoop(cue, intensity);
            return;
        }

        if (_lastTriggerSeconds.TryGetValue(cue, out var last) && _clockSeconds - last < spec.MinIntervalSeconds) return;
        _lastTriggerSeconds[cue] = _clockSeconds;
        ReleaseExcessInstances(spec);
        StartVoice(spec, intensity);
    }

    /// <summary>Starts a loop cue, or updates its intensity when it is already running.</summary>
    public void StartLoop(AudioCue cue, float intensity = 1f)
    {
        var spec = CueTable.Get(cue);
        if (!spec.IsLoop) throw new ArgumentException($"{cue} is not a loop cue.", nameof(cue));
        if (TryGetLoop(cue, out var running))
        {
            running.Intensity = intensity;
            return;
        }

        var player = StartVoice(spec, intensity);
        if (player?.Generator is { } generator) _loops[cue] = (player, generator);
    }

    /// <summary>0..1; drives level and, for the entry roar, brightness. Smoothed by the generator.</summary>
    public void SetLoopIntensity(AudioCue cue, float intensity)
    {
        if (TryGetLoop(cue, out var running)) running.Intensity = intensity;
    }

    /// <summary>Stops a loop with its own fade-out.</summary>
    public void StopLoop(AudioCue cue)
    {
        if (TryGetLoop(cue, out var running)) running.Stop();
        _loops.Remove(cue);
    }

    /// <summary>Raises an alarm or changes its priority (de-escalation included); forward every simulation <c>AlarmRaised</c>.</summary>
    public void RaiseAlarm(string alarmId, AlarmPriority priority)
    {
        _alarms.Raise(alarmId, priority);
        if (!_frozen) AdvanceAlarms(0.0);
    }

    public void ClearAlarm(string alarmId)
    {
        _alarms.Clear(alarmId);
        if (!_frozen) AdvanceAlarms(0.0);
    }

    /// <summary>Silences sounding alarms; the master alarm continues as an attenuated reminder while it persists.</summary>
    public void AcknowledgeAlarms()
    {
        _alarms.AcknowledgeAll();
        if (!_frozen) AdvanceAlarms(0.0);
    }

    /// <summary>
    /// Follows the app's one freeze signal (Esc menu, outage, loading): alarm cadence (warning repeats, master reminders)
    /// stops counting, and every voice except UI pauses where it is, loops included, so nothing sounds or re-fires under
    /// the menu or the outage overlay. Alarm changes made while frozen sound on unfreeze.
    /// </summary>
    public void SetFrozen(bool frozen)
    {
        if (_frozen == frozen) return;
        _frozen = frozen;
        foreach (var (bus, pool) in _pools)
        {
            if (bus == AudioBus.Ui) continue;
            foreach (var player in pool)
            {
                player.StreamPaused = frozen && player.IsActive;
            }
        }
    }

    /// <summary>Player volume for a bus, 0..1 (settings screen). 0 mutes the bus.</summary>
    public void SetBusVolume(AudioBus bus, float linear)
    {
        var volume = Math.Clamp(linear, 0f, 1f);
        _playerVolumes[bus] = volume;
        var index = AudioBusSetup.IndexOf(bus);
        if (index < 0) return;
        AudioServer.SetBusMute(index, volume <= 0f);
        if (volume > 0f) AudioServer.SetBusVolumeDb(index, _mix.Bus(bus).LevelDb + Decibels.FromGain(volume));
    }

    public float GetBusVolume(AudioBus bus) => _playerVolumes.GetValueOrDefault(bus, 1f);

    /// <summary>Hard stop of every voice (scene change, quit to menu).</summary>
    public void StopAll()
    {
        foreach (var pool in _pools.Values)
        {
            foreach (var player in pool)
            {
                player.Silence();
            }
        }

        _loops.Clear();
        _alarms.Reset();
        _lastTriggerSeconds.Clear();
    }

    private void AdvanceAlarms(double deltaSeconds)
    {
        _alarmActions.Clear();
        _alarms.Advance(deltaSeconds, _alarmActions);
        foreach (var action in _alarmActions)
        {
            switch (action.Kind)
            {
                case AlarmActionKind.PlayOnce:
                    Play(action.Cue);
                    break;
                case AlarmActionKind.StartLoop:
                    StartLoop(action.Cue);
                    break;
                default:
                    StopLoop(action.Cue);
                    break;
            }
        }
    }

    private AudioSynthPlayer? StartVoice(CueSpec spec, float intensity)
    {
        if (!_pools.TryGetValue(spec.Bus, out var pool)) return null;
        Span<VoiceSlot> slots = stackalloc VoiceSlot[pool.Length];
        for (var i = 0; i < pool.Length; i++)
        {
            slots[i] = new VoiceSlot(pool[i].IsActive, pool[i].Priority, pool[i].StartSequence);
        }

        var index = VoiceAllocator.Choose(slots, spec.Priority);
        if (index < 0) return null;
        var player = pool[index];
        player.Start(_factory.Create(spec.Sound, _seed++, intensity), _busNames[spec.Bus], spec.GainDb, spec.Priority, ++_sequence);
        if (_frozen && spec.Bus != AudioBus.Ui) player.StreamPaused = true;
        return player;
    }

    /// <summary>Fades out the oldest instances of the cue's sound so the new one stays within its cap.</summary>
    private void ReleaseExcessInstances(CueSpec spec)
    {
        var pool = _pools[spec.Bus];
        var count = 0;
        foreach (var player in pool)
        {
            if (IsPlaying(player, spec.Sound)) count++;
        }

        var releasedUpTo = long.MinValue;
        for (var excess = count - spec.MaxInstances + 1; excess > 0; excess--)
        {
            AudioSynthPlayer? oldest = null;
            foreach (var player in pool)
            {
                if (IsPlaying(player, spec.Sound) && player.StartSequence > releasedUpTo
                    && (oldest is null || player.StartSequence < oldest.StartSequence))
                {
                    oldest = player;
                }
            }

            if (oldest is null) return;
            oldest.Release();
            releasedUpTo = oldest.StartSequence;
        }
    }

    private static bool IsPlaying(AudioSynthPlayer player, SoundId sound) =>
        player.Generator is { IsFinished: false } generator && generator.Id == sound;

    /// <summary>A loop's voice may have been stolen by a higher-priority sound; then the loop is gone.</summary>
    private bool TryGetLoop(AudioCue cue, [MaybeNullWhen(false)] out ISoundGenerator generator)
    {
        if (_loops.TryGetValue(cue, out var loop) && ReferenceEquals(loop.Player.Generator, loop.Generator) && !loop.Generator.IsFinished)
        {
            generator = loop.Generator;
            return true;
        }

        _loops.Remove(cue);
        generator = null;
        return false;
    }
}
