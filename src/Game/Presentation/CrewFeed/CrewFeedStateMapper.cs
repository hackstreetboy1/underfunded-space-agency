using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>
/// One crew member's comm feed: picks the AnimationTree state and blend parameters, fires gestures, and drives the
/// face (expression cross-fade, blinks, lip-flap) from that member's condition and activity. Time comes from the
/// injected clock and randomness from the seed, so equal inputs give equal frames.
/// </summary>
/// <remarks>
/// State priority, strongest first: g-load (pinned to the couch), impaired (Impaired or Critical band), spin strain,
/// talk, read gauge, work, listen, stressed, idle. Gestures play only over calm states (idle, listen, talk, work, read
/// gauge, stressed) and are dropped otherwise or when the same gesture is still playing. A dead crew member is lost:
/// a neutral face that never changes again.
/// </remarks>
public sealed class CrewFeedStateMapper
{
    private readonly IMonotonicClock _clock;
    private readonly CrewFeedSettings _settings;
    private readonly ExpressionBlender _blender;
    private readonly BlinkGenerator _blink;
    private readonly LipFlap _lip;
    private readonly FaceWeights _face = new();
    private readonly double[] _oneShotEndsAt = new double[4];
    private CrewFeedOneShot _pendingOneShot;
    private double _alarmFaceRemaining;
    private double _reliefFaceRemaining;
    private double _lastSeconds;
    private bool _started;
    private bool _spinActive;
    private bool _gActive;
    private float _spinIntensity;
    private float _gIntensity;

    /// <param name="seed">Mission seed; blink and lip-flap streams are derived from it and <paramref name="crewId"/>.</param>
    public CrewFeedStateMapper(ExpressionSet expressions, IMonotonicClock clock, ulong seed, string crewId, CrewFeedSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrEmpty(crewId);
        _clock = clock;
        _settings = settings ?? CrewFeedSettings.Default;
        _blender = new ExpressionBlender(expressions);
        _blink = new BlinkGenerator(Pcg32.ForStream(seed, $"crew_feed.{crewId}.blink"), _settings);
        _lip = new LipFlap(Pcg32.ForStream(seed, $"crew_feed.{crewId}.lip"), _settings);
        Current = new CrewFeedFrame(CrewFeedState.Idle, 0f, 0f, CrewFeedOneShot.None, ExpressionPreset.Neutral, _face, Lost: false);
    }

    public CrewFeedFrame Current { get; private set; }

    /// <summary>The input of the latest <see cref="Update"/> (debug overlays, tests).</summary>
    public CrewFeedInput LastInput { get; private set; }

    public BlinkGenerator Blink => _blink;

    public LipFlap Lip => _lip;

    /// <summary>Queues a reaction; it applies on the next <see cref="Update"/>.</summary>
    public void Notify(CrewFeedEvent feedEvent)
    {
        switch (feedEvent)
        {
            case CrewFeedEvent.Alarm:
                _alarmFaceRemaining = _settings.AlarmFaceSeconds;
                _reliefFaceRemaining = 0;
                break;
            case CrewFeedEvent.Relief:
                _reliefFaceRemaining = _settings.ReliefFaceSeconds;
                _alarmFaceRemaining = 0;
                Queue(CrewFeedOneShot.Relief);
                break;
            case CrewFeedEvent.FirstContact:
                Queue(CrewFeedOneShot.Wave);
                break;
            case CrewFeedEvent.Acknowledge:
                Queue(CrewFeedOneShot.Nod);
                break;
        }
    }

    public CrewFeedFrame Update(in CrewFeedInput input)
    {
        LastInput = input;
        var now = _clock.Elapsed.TotalSeconds;
        var dt = _started ? Math.Clamp(now - _lastSeconds, 0.0, _settings.MaxStepSeconds) : 0.0;
        _started = true;
        _lastSeconds = now;
        if (Current.Lost) return Current;
        if (!input.Alive || input.Health == HealthBand.Deceased) return Lose();

        if (input.RecentEvent != CrewFeedEvent.None) Notify(input.RecentEvent);
        var alarmFace = _alarmFaceRemaining > 0;
        var reliefFace = _reliefFaceRemaining > 0;
        _alarmFaceRemaining = Math.Max(0, _alarmFaceRemaining - dt);
        _reliefFaceRemaining = Math.Max(0, _reliefFaceRemaining - dt);

        var rate = Math.Abs(input.AngularRateDegPerSec);
        _spinActive = rate >= _settings.SpinEnterDps || (_spinActive && rate > _settings.SpinExitDps);
        _gActive = input.GLoad >= _settings.GLoadEnter || (_gActive && input.GLoad > _settings.GLoadExit);
        var impaired = input.Health is HealthBand.Impaired or HealthBand.Critical;

        var spinTarget = _spinActive ? (float)Math.Clamp(rate / _settings.SpinFullDps, 0.0, 1.0) : 0f;
        var gTarget = _gActive ? (float)Math.Clamp((input.GLoad - 1.0) / (_settings.GLoadFull - 1.0), 0.0, 1.0) : 0f;
        _spinIntensity = Smoothing.Approach(_spinIntensity, spinTarget, dt, _settings.IntensityTimeConstantSeconds);
        _gIntensity = Smoothing.Approach(_gIntensity, gTarget, dt, _settings.IntensityTimeConstantSeconds);

        var state = SelectState(input, impaired);
        var oneShot = FireOneShot(state, now);
        var expression = SelectExpression(input, impaired, rate, alarmFace, reliefFace);
        _blender.Update(expression, dt, _settings.ExpressionTimeConstantSeconds);
        var blink = _blink.Update(dt, impaired);
        _lip.Update(dt, input.Speaking, input.SpeechLevelDbfs);
        ComposeFace(blink);

        Current = new CrewFeedFrame(state, _spinIntensity, _gIntensity, oneShot, expression, _face, Lost: false);
        return Current;
    }

    private CrewFeedState SelectState(in CrewFeedInput input, bool impaired)
    {
        if (_gActive) return CrewFeedState.GLoad;
        if (impaired) return CrewFeedState.Impaired;
        if (_spinActive) return CrewFeedState.SpinStrain;
        if (input.Speaking) return CrewFeedState.Talk;
        if (input.Activity == CrewActivity.GlanceRead) return CrewFeedState.ReadGauge;
        if (input.Activity.IsHandsOn()) return CrewFeedState.Work;
        if (input.Listening) return CrewFeedState.Listen;
        return input.Health == HealthBand.Stressed ? CrewFeedState.Stressed : CrewFeedState.Idle;
    }

    private ExpressionPreset SelectExpression(in CrewFeedInput input, bool impaired, double rate, bool alarmFace, bool reliefFace)
    {
        if (impaired) return ExpressionPreset.Impaired;
        if (_gActive) return ExpressionPreset.Strained;
        if (alarmFace) return ExpressionPreset.Alarmed;
        if (_spinActive) return rate >= _settings.SpinStrainedFaceDps ? ExpressionPreset.Strained : ExpressionPreset.Concerned;
        if (reliefFace) return ExpressionPreset.Relieved;
        if (input.Health == HealthBand.Stressed) return ExpressionPreset.Concerned;
        if (input.Activity != CrewActivity.None) return ExpressionPreset.Focused;
        return ExpressionPreset.Neutral;
    }

    private void Queue(CrewFeedOneShot oneShot)
    {
        // Relief outranks a wave, a wave outranks a nod, when several arrive in one frame.
        if (oneShot > _pendingOneShot) _pendingOneShot = oneShot;
    }

    private CrewFeedOneShot FireOneShot(CrewFeedState state, double nowSeconds)
    {
        var pending = _pendingOneShot;
        _pendingOneShot = CrewFeedOneShot.None;
        if (pending == CrewFeedOneShot.None) return pending;
        var calm = state is CrewFeedState.Idle or CrewFeedState.Listen or CrewFeedState.Talk
            or CrewFeedState.Work or CrewFeedState.ReadGauge or CrewFeedState.Stressed;
        if (!calm || nowSeconds < _oneShotEndsAt[(int)pending]) return CrewFeedOneShot.None;
        _oneShotEndsAt[(int)pending] = nowSeconds + CrewFeedClips.DurationSeconds(pending);
        return pending;
    }

    private void ComposeFace(float blink)
    {
        for (var k = 0; k < ShapeKeys.Count; k++)
        {
            var key = (ShapeKey)k;
            _face[key] = _blender.Weight(key);
        }

        _face[ShapeKey.Blink] = Math.Max(_face[ShapeKey.Blink], blink);
        _face[ShapeKey.MouthOpen] += _lip.MouthOpen;
        _face[ShapeKey.MouthO] += _lip.MouthO;
    }

    private CrewFeedFrame Lose()
    {
        _blender.Snap(ExpressionPreset.Neutral);
        _face.Clear();
        _pendingOneShot = CrewFeedOneShot.None;
        Current = new CrewFeedFrame(CrewFeedState.Idle, 0f, 0f, CrewFeedOneShot.None, ExpressionPreset.Neutral, _face, Lost: true);
        return Current;
    }
}
