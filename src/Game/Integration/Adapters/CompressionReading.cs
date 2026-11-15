using Usa.Simulation.Time;

namespace Usa.Game.Integration.Adapters;

/// <summary>The host pacer's time-compression state (rate, reason, advance button), read each frame for the header.</summary>
public readonly record struct CompressionReading(double Rate, CompressionReason Reason, bool CanAdvanceToNextEvent, bool IsAdvancingToEvent)
{
    public static CompressionReading RealTime { get; } = new(1, CompressionReason.StableCoast, false, false);
}
