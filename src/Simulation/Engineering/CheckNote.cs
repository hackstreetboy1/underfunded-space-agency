namespace Usa.Simulation.Engineering;

/// <summary>
/// What one finding of a check says. The workspace words each note from <c>workspace.check*</c> strings; the
/// argument names each note carries are listed with it.
/// </summary>
public enum CheckNote
{
    /// <summary><c>source</c>, <c>sink</c>.</summary>
    ContinuityOk,

    /// <summary><c>source</c>.</summary>
    SourceNotConnected,

    /// <summary>The circuit ends at an open connector: <c>source</c>, <c>connector</c>, <c>part</c>, <c>sink</c>.</summary>
    OpenEnd,

    /// <summary>The circuit ends without an open connector (a loop): <c>source</c>, <c>sink</c>.</summary>
    OpenPartway,

    ConnectorsMate,

    /// <summary>One mismatched link: <c>connector</c>, <c>at</c>, <c>otherConnector</c>, <c>other</c> (<c>at</c> and <c>other</c> are references).</summary>
    ConnectorMismatch,

    /// <summary>Follows the mismatches.</summary>
    AdapterHint,

    /// <summary><c>part</c>, <c>rating</c>, <c>load</c>, <c>limit</c>; the limit is the goal's wiring.</summary>
    ProtectionOk,

    /// <summary><c>part</c>, <c>rating</c>, <c>load</c>, <c>limit</c>, <c>conductor</c>; the limit is a placed conductor.</summary>
    ProtectionOkByPart,

    /// <summary>A fuse or breaker at or below the load: <c>part</c>, <c>rating</c>, <c>load</c>.</summary>
    ProtectionUnderrated,

    /// <summary>A conductor below the load: <c>part</c>, <c>rating</c>, <c>load</c>.</summary>
    PartUnderrated,

    /// <summary><c>load</c>, <c>limit</c>.</summary>
    NoProtection,

    /// <summary><c>part</c>, <c>rating</c>, <c>limit</c>; the limit is the goal's wiring.</summary>
    ProtectionOverrated,

    /// <summary><c>part</c>, <c>rating</c>, <c>limit</c>, <c>conductor</c>; the limit is a placed conductor.</summary>
    ProtectionOverratedByPart,

    /// <summary><c>length</c>, <c>minimum</c>.</summary>
    LengthOk,

    /// <summary><c>length</c>, <c>minimum</c>.</summary>
    TooShort,

    /// <summary><c>connector</c>, <c>part</c>.</summary>
    LiveOpenPort,

    /// <summary><c>parts</c> (names joined with commas).</summary>
    StrayParts,

    NoLiveOpenPort,

    AllPartsOnBoard,

    /// <summary><c>count</c>, <c>part</c>.</summary>
    NoneOnBoard,

    /// <summary><c>count</c>, <c>part</c>.</summary>
    OneOnBoard,

    /// <summary><c>count</c>, <c>part</c>, <c>available</c>.</summary>
    SomeOnBoard,

    /// <summary>The check was skipped.</summary>
    NeedsContinuity,
}
