using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>A tile's 60-minute sparkline, oldest sample first, one per MET minute.</summary>
/// <param name="LastSampleAt">Start of the MET minute of the newest sample; null when empty.</param>
/// <param name="Samples">Null entries are gaps: the value was not known (fresh) at that minute.</param>
public sealed record SparklineSeries(MissionTime? LastSampleAt, IReadOnlyList<double?> Samples)
{
    public static SparklineSeries Empty { get; } = new(null, []);

    public double? Min => Extreme(static (a, b) => a < b);

    public double? Max => Extreme(static (a, b) => a > b);

    private double? Extreme(Func<double, double, bool> better)
    {
        double? result = null;
        foreach (var sample in Samples)
        {
            if (sample is { } value && (result is not { } current || better(value, current))) result = value;
        }
        return result;
    }
}
