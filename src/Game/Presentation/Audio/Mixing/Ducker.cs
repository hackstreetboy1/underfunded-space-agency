namespace Usa.Game.Presentation.Audio.Mixing;

/// <summary>
/// Gain follower for ducking Ambience under Speech: keyed by the Speech-bus peak, it attacks to
/// <c>-DepthDb</c>, holds through short pauses between words, then releases.
/// </summary>
public sealed class Ducker
{
    private readonly DuckingSettings _settings;
    private double _holdRemaining;

    public Ducker(DuckingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>Current gain offset in dB (0 or negative).</summary>
    public float GainDb { get; private set; }

    public float Update(float keyLevelDb, double deltaSeconds)
    {
        var keyed = keyLevelDb > _settings.KeyThresholdDb;
        _holdRemaining = keyed ? _settings.HoldSeconds : _holdRemaining - deltaSeconds;
        var target = keyed || _holdRemaining > 0.0 ? -_settings.DepthDb : 0f;
        var timeConstant = target < GainDb ? _settings.AttackSeconds : _settings.ReleaseSeconds;
        var blend = 1.0 - Math.Exp(-deltaSeconds / timeConstant);
        GainDb += (float)((target - GainDb) * blend);
        return GainDb;
    }
}
