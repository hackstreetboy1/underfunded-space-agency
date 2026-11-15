namespace Usa.Game.Presentation.CrewFeed;

/// <summary>
/// Cross-fades expression presets: a mix weight per preset that always sums to 1 and eases toward the target preset.
/// A key's weight is the mix-weighted sum of the presets' values, so it stays within 0..1.
/// </summary>
public sealed class ExpressionBlender
{
    private readonly ExpressionSet _expressions;
    private readonly float[] _mix = new float[ExpressionPresets.Count];

    public ExpressionBlender(ExpressionSet expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        _expressions = expressions;
        _mix[(int)ExpressionPreset.Neutral] = 1f;
    }

    public ExpressionPreset Target { get; private set; }

    public float Mix(ExpressionPreset preset) => _mix[(int)preset];

    public void Update(ExpressionPreset target, double deltaSeconds, double timeConstantSeconds)
    {
        Target = target;
        var factor = Smoothing.Factor(deltaSeconds, timeConstantSeconds);
        var sum = 0f;
        for (var p = 0; p < _mix.Length; p++)
        {
            var goal = p == (int)target ? 1f : 0f;
            _mix[p] += (goal - _mix[p]) * factor;
            sum += _mix[p];
        }

        for (var p = 0; p < _mix.Length; p++) _mix[p] /= sum;
    }

    public void Snap(ExpressionPreset preset)
    {
        Target = preset;
        Array.Clear(_mix);
        _mix[(int)preset] = 1f;
    }

    public float Weight(ShapeKey key)
    {
        var weight = 0f;
        for (var p = 0; p < _mix.Length; p++) weight += _mix[p] * _expressions.Weight((ExpressionPreset)p, key);
        return Math.Clamp(weight, 0f, 1f);
    }
}
