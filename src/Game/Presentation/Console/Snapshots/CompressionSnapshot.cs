namespace Usa.Game.Presentation.Console.Snapshots;

/// <param name="Rate">MET seconds per real second the director asks for (1 = real time, 0 = frozen).</param>
public sealed record CompressionSnapshot(double Rate, CompressionCause Reason)
{
    public static CompressionSnapshot RealTime { get; } = new(1, CompressionCause.None);

    /// <summary>The vehicle is stable, so "Advance to next event" may be pressed.</summary>
    public bool CanAdvanceToNextEvent { get; init; }

    public bool IsAdvancingToEvent { get; init; }
}
