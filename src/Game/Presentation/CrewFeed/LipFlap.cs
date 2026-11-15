using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>
/// Mouth weights from the voice output level: gated at ~-30 dBFS, opened in proportion to level above the gate,
/// smoothed with a fast attack and slower release. Each time the gate opens (a new syllable) a seeded share of the
/// opening goes to <c>mouth_o</c>, so the mouth doesn't flap identically.
/// </summary>
public sealed class LipFlap
{
    private readonly Pcg32 _random;
    private readonly CrewFeedSettings _settings;
    private float _envelope;
    private float _oShare;

    public LipFlap(Pcg32 random, CrewFeedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(settings);
        _random = random;
        _settings = settings;
    }

    public bool GateOpen { get; private set; }

    /// <summary>Total opening 0..1 (<see cref="MouthOpen"/> + <see cref="MouthO"/>).</summary>
    public float Envelope => _envelope;

    public float MouthOpen { get; private set; }

    public float MouthO { get; private set; }

    public void Update(double deltaSeconds, bool speaking, double levelDbfs)
    {
        var open = speaking && levelDbfs >= _settings.LipGateDbfs;
        if (open && !GateOpen) _oShare = (float)(_random.NextDouble() * _settings.LipMaxOShare);
        GateOpen = open;

        var target = 0f;
        if (open)
        {
            var span = _settings.LipFullDbfs - _settings.LipGateDbfs;
            var level = span > 0.0 ? (float)Math.Clamp((levelDbfs - _settings.LipGateDbfs) / span, 0.0, 1.0) : 1f;
            target = _settings.LipMinOpen + ((1f - _settings.LipMinOpen) * level);
        }

        var timeConstant = target > _envelope ? _settings.LipAttackSeconds : _settings.LipReleaseSeconds;
        _envelope = Smoothing.Approach(_envelope, target, deltaSeconds, timeConstant);
        if (_envelope < 1e-4f) _envelope = 0f;
        MouthO = _envelope * _oShare;
        MouthOpen = _envelope - MouthO;
    }
}
