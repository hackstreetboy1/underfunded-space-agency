namespace Usa.Simulation.Engineering;

public enum CheckReason
{
    None,
    SourceNotConnected,
    OpenCircuit,
    ConnectorMismatch,
    NoProtection,

    /// <summary>A fuse or breaker rated below the load current; it would trip in normal use.</summary>
    ProtectionUnderrated,

    /// <summary>The lowest-rated protection is above the wire rating; the wire would fail first.</summary>
    ProtectionOverrated,

    /// <summary>A harness or adapter rated below the load current.</summary>
    PartUnderrated,
    TooShort,

    /// <summary>An energised connector left unconnected (the circuit from the source ends in the open).</summary>
    LiveOpenPort,

    /// <summary>A placed part that is not in the source-to-sink circuit; it would be left loose in the cabin.</summary>
    StrayPart,
    NotEnoughParts,
    NeedsContinuity,
}
