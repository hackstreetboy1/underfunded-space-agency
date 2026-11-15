using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Integration.Runtime;

/// <summary>What the composed runtime reads from and asks of the simulation host and the connection monitor.</summary>
public interface IMissionHostControl
{
    /// <summary>The pacer's time-compression state for the console header.</summary>
    CompressionReading Compression { get; }

    LinkState Link { get; }

    /// <summary>The header's "advance to next event"; false when the pacer refuses.</summary>
    bool RequestAdvanceToNextEvent();
}
