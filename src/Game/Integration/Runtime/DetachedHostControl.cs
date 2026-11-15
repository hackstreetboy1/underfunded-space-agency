using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Integration.Runtime;

/// <summary>No host attached (tests, tools): real time, link up, advancing refused.</summary>
public sealed class DetachedHostControl : IMissionHostControl
{
    public static DetachedHostControl Instance { get; } = new();

    public CompressionReading Compression => CompressionReading.RealTime;

    public LinkState Link => LinkState.Ok;

    public bool RequestAdvanceToNextEvent() => false;
}
