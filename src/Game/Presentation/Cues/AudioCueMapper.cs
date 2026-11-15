using System.Globalization;
using Usa.Game.Presentation.Audio.Mixing;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Attitude;
using Usa.Simulation.Commands;
using Usa.Simulation.Flight;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle;

namespace Usa.Game.Presentation.Cues;

/// <summary>
/// Turns simulation events and a per-frame state sample into audio commands (GDD 14, UI.md 3.10, gate-7 MIX.md).
/// </summary>
/// <remarks>
/// <para>Alarms mirror the session's annunciator: each id is raised once per level (a lower level too; the director's
/// sequencer handles de-escalation) and cleared once. Acknowledgement is forwarded only while something is active. Rate
/// advisories tick only for the crewed vehicle, the one whose rate the crew reports (GDD 4.2: sound shows nothing the
/// player could not know).</para>
/// <para>Loops (burn rumble, entry roar, cabin bed) are level-driven: the mapper starts, re-levels
/// and stops each one only when its wanted state changes, so the sink sees no repeats. Thruster pulses go through a
/// token bucket; event one-shots have a minimum repeat interval on top of the director's own anti-retrigger gap.</para>
/// </remarks>
public sealed class AudioCueMapper
{
    private static readonly AudioCue[] ManagedLoops =
        [AudioCue.CabinAmbience, AudioCue.RetroBurn, AudioCue.EntryRoar];

    private readonly IAudioSink _sink;
    private readonly IMonotonicClock _clock;
    private readonly AudioCueSettings _settings;
    private readonly string? _crewedVehicleId;
    private readonly Dictionary<string, AlarmPriority> _alarms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _advisoryAt = new(StringComparer.Ordinal);
    private readonly Dictionary<AudioCue, double> _oneShotAt = [];
    private readonly Dictionary<AudioCue, float> _runningLoops = [];
    private readonly HashSet<string> _burningVehicles = new(StringComparer.Ordinal);
    private readonly HashSet<ComponentId> _brownedOutBuses = [];
    private readonly PulseLimiter _pulses;
    private AudioStateSample _sample;
    private bool _sampled;
    private bool _retroFiring;
    private bool _inAtmosphere;
    private bool _ended;
    private bool _landed;

    /// <param name="crewedVehicleId">The crewed vehicle (<c>Flight.Vehicles.CrewedId</c>); rate advisories tick only for it,
    /// and not at all without it.</param>
    public AudioCueMapper(IAudioSink sink, IMonotonicClock clock, AudioCueSettings? settings = null, string? crewedVehicleId = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(clock);
        _sink = sink;
        _clock = clock;
        _settings = settings ?? AudioCueSettings.Default;
        _crewedVehicleId = crewedVehicleId;
        _pulses = new PulseLimiter(_settings.PulseBurst);
    }

    /// <summary>Alarm ids the mapper has raised and not yet cleared.</summary>
    public int ActiveAlarmCount => _alarms.Count;

    public bool IsLoopRunning(AudioCue cue) => _runningLoops.ContainsKey(cue);

    private double NowSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>Handles one drained simulation event; unrelated events are ignored.</summary>
    public void Handle(ISimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        switch (simEvent)
        {
            case AlarmRaised raised:
                RaiseAlarm(raised.AlarmId, ToPriority(raised.Level));
                break;
            case AlarmCleared cleared:
                _acknowledged.Remove(cleared.AlarmId);
                if (_alarms.Remove(cleared.AlarmId)) _sink.ClearAlarm(cleared.AlarmId);
                break;
            case AlarmsAcknowledged:
                _acknowledged.UnionWith(_alarms.Keys);
                if (_alarms.Count > 0) _sink.AcknowledgeAlarms();
                break;
            case RateThresholdCrossed { Rising: true } crossed when crossed.VehicleId == _crewedVehicleId:
                RaiseAdvisory($"rate.{crossed.VehicleId}.{crossed.ThresholdDegPerSec.ToString("0.#", CultureInfo.InvariantCulture)}");
                break;
            case BurnStarted burn:
                _burningVehicles.Add(burn.VehicleId);
                UpdateLoops();
                break;
            case BurnEnded burn:
                _burningVehicles.Remove(burn.VehicleId);
                if (_burningVehicles.Count == 0) _retroFiring = false;
                UpdateLoops();
                break;
            case ComponentStateChanged { Field: ComponentFields.Fired } fired when fired.Value is { Kind: StateValueKind.Bool } value && value.AsBool():
                _retroFiring = true;
                UpdateLoops();
                break;
            case AtmosphereInterfaceCrossed crossing:
                _inAtmosphere = crossing.Entering;
                _landed &= !crossing.Entering;
                UpdateLoops();
                break;
            case ChuteDeployed chute:
                OnChute(chute);
                break;
            case SectionJettisoned:
                PlayOnce(AudioCue.ChuteDeploy, _settings.JettisonIntensity);
                break;
            case Splashdown splash:
                OnSplashdown(splash);
                break;
            case BusBrownoutChanged brownout:
                if (brownout.InBrownout) _brownedOutBuses.Add(brownout.Bus);
                else _brownedOutBuses.Remove(brownout.Bus);
                UpdateLoops();
                break;
            case TipAvailable:
                PlayOnce(AudioCue.TipShown, 1f);
                break;
            case CommandAccepted:
                PlayOnce(AudioCue.UiConfirm, 1f);
                break;
            case CommandRefused:
                PlayOnce(AudioCue.UiError, 1f);
                break;
            case MissionEnded:
                _ended = true;
                UpdateLoops();
                break;
        }
    }

    /// <summary>
    /// A resume's replayed event (<c>MissionHistory</c>): rebuilds the alarm set (level and acknowledgement) without a sound.
    /// Call <see cref="ResumeAlarms"/> once after the last one.
    /// </summary>
    public void Restore(ISimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        switch (simEvent)
        {
            case AlarmRaised raised:
                var priority = ToPriority(raised.Level);
                if (_alarms.TryGetValue(raised.AlarmId, out var current) && current == priority) break;
                _alarms[raised.AlarmId] = priority;
                _acknowledged.Remove(raised.AlarmId);
                break;
            case AlarmCleared cleared:
                _alarms.Remove(cleared.AlarmId);
                _acknowledged.Remove(cleared.AlarmId);
                break;
            case AlarmsAcknowledged:
                _acknowledged.UnionWith(_alarms.Keys);
                break;
            case MissionEnded:
                _ended = true;
                break;
        }
    }

    /// <summary>
    /// After a resume: the alarm sound the uninterrupted run had at the save. Acknowledged alarms are raised and acknowledged
    /// (attenuated reminder only), then the unacknowledged ones resume sounding at their level. Nothing else is played.
    /// </summary>
    public void ResumeAlarms()
    {
        var ids = _alarms.Keys.ToArray();
        Array.Sort(ids, StringComparer.Ordinal);
        var acknowledged = false;
        foreach (var id in ids)
        {
            if (!_acknowledged.Contains(id)) continue;
            _sink.RaiseAlarm(id, _alarms[id]);
            acknowledged = true;
        }
        if (acknowledged) _sink.AcknowledgeAlarms();
        foreach (var id in ids)
        {
            if (!_acknowledged.Contains(id)) _sink.RaiseAlarm(id, _alarms[id]);
        }
    }

    /// <summary>Applies the frame's continuous state: loops and their intensities, thruster pulses.</summary>
    public void Sample(in AudioStateSample sample)
    {
        _sample = sample;
        _sampled = true;
        UpdateLoops();

        var rate = sample.Docked ? _settings.DockedPulsesPerSecond : _settings.PulsesPerSecond;
        var granted = _ended ? 0 : _pulses.Take(sample.AttitudePulses, NowSeconds, rate);
        for (var i = 0; i < granted; i++) _sink.Play(AudioCue.ThrusterPulse, _settings.PulseIntensity);
    }

    /// <summary>Stops the loops and clears the alarms this mapper started (session closed, scene change).</summary>
    public void Reset()
    {
        foreach (var cue in ManagedLoops)
        {
            if (_runningLoops.Remove(cue)) _sink.StopLoop(cue);
        }

        var ids = _alarms.Keys.ToArray();
        Array.Sort(ids, StringComparer.Ordinal);
        foreach (var id in ids) _sink.ClearAlarm(id);
        _alarms.Clear();
        _acknowledged.Clear();
        _advisoryAt.Clear();
        _oneShotAt.Clear();
        _burningVehicles.Clear();
        _brownedOutBuses.Clear();
        _sample = default;
        _sampled = _retroFiring = _inAtmosphere = _ended = _landed = false;
    }

    private static AlarmPriority ToPriority(AlarmLevel level) => level switch
    {
        AlarmLevel.Caution => AlarmPriority.Caution,
        AlarmLevel.Warning => AlarmPriority.Warning,
        AlarmLevel.Master => AlarmPriority.Master,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private void RaiseAlarm(string alarmId, AlarmPriority priority)
    {
        if (_alarms.TryGetValue(alarmId, out var current) && current == priority) return;
        _alarms[alarmId] = priority;
        _acknowledged.Remove(alarmId);
        _sink.RaiseAlarm(alarmId, priority);
    }

    private void RaiseAdvisory(string advisoryId)
    {
        var now = NowSeconds;
        if (_advisoryAt.TryGetValue(advisoryId, out var last) && now - last < _settings.AdvisoryMinIntervalSeconds) return;
        _advisoryAt[advisoryId] = now;
        _sink.RaiseAlarm(advisoryId, AlarmPriority.Advisory);
    }

    private void OnChute(ChuteDeployed chute)
    {
        var intensity = chute.Result switch
        {
            ChuteDeployResult.Deployed => chute.Chute == ChuteKind.Drogue ? _settings.DrogueIntensity : _settings.MainChuteIntensity,
            ChuteDeployResult.TooHigh or ChuteDeployResult.TooFast => _settings.FailedChuteIntensity,
            _ => 0f,
        };
        if (intensity > 0f) PlayOnce(AudioCue.ChuteDeploy, intensity);
    }

    private void OnSplashdown(Splashdown splash)
    {
        var speed = Math.Clamp(splash.ImpactSpeedMps / _settings.SplashSpeedForFullIntensityMps, 0.0, 1.0);
        PlayOnce(AudioCue.Splashdown, Math.Max(_settings.SplashMinIntensity, (float)speed));
        _inAtmosphere = false;
        _landed = true;
        _burningVehicles.Clear();
        _retroFiring = false;
        UpdateLoops();
    }

    private void PlayOnce(AudioCue cue, float intensity)
    {
        var now = NowSeconds;
        if (_oneShotAt.TryGetValue(cue, out var last) && now - last < _settings.OneShotMinIntervalSeconds) return;
        _oneShotAt[cue] = now;
        _sink.Play(cue, Math.Clamp(intensity, 0f, 1f));
    }

    private void UpdateLoops()
    {
        var live = !_ended;
        SetLoop(AudioCue.CabinAmbience, live && _sampled && _settings.ManageCabinAmbience,
            _brownedOutBuses.Count > 0 ? _settings.BrownoutAmbienceIntensity : _settings.CabinAmbienceIntensity);

        SetLoop(AudioCue.RetroBurn, live && (_burningVehicles.Count > 0 || _sample.BurnActive),
            _retroFiring ? _settings.RetroBurnIntensity : _settings.PoweredBurnIntensity);

        var heating = (float)Math.Clamp(_sample.EntryHeatingIntensity, 0.0, 1.0);
        var roarWanted = _inAtmosphere
            || heating >= _settings.EntryRoarOnIntensity
            || (_runningLoops.ContainsKey(AudioCue.EntryRoar) && heating > _settings.EntryRoarOffIntensity);
        SetLoop(AudioCue.EntryRoar, live && !_landed && roarWanted, Math.Max(heating, _settings.EntryRoarFloorIntensity));
    }

    private void SetLoop(AudioCue cue, bool wanted, float intensity)
    {
        intensity = Math.Clamp(intensity, 0f, 1f);
        var running = _runningLoops.TryGetValue(cue, out var sent);
        if (!wanted)
        {
            if (!running) return;
            _runningLoops.Remove(cue);
            _sink.StopLoop(cue);
            return;
        }

        if (!running)
        {
            _runningLoops[cue] = intensity;
            _sink.StartLoop(cue, intensity);
            return;
        }

        var reachesEnd = intensity is 0f or 1f && intensity != sent;
        if (!reachesEnd && Math.Abs(intensity - sent) < _settings.LoopIntensityEpsilon) return;
        _runningLoops[cue] = intensity;
        _sink.SetLoopIntensity(cue, intensity);
    }
}
