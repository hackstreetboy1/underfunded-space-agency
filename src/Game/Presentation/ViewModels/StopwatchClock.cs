using System.Diagnostics;

namespace Usa.Game.Presentation.ViewModels;

/// <summary>High-resolution <see cref="IMonotonicClock"/> backed by <see cref="Stopwatch"/>.</summary>
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly long _originTimestamp = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_originTimestamp);
}
