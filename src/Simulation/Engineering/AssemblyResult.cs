namespace Usa.Simulation.Engineering;

/// <summary>Why a workspace edit was refused.</summary>
public enum AssemblyFailure
{
    None,
    UnknownPart,

    /// <summary>The part has no connectors (tape, canisters) and can't carry current.</summary>
    NotAnAssemblyPart,
    UnknownInstance,
    UnknownPort,
    PortInUse,
    SamePart,
    NotConnected,

    /// <summary>The goal's source and sink are fixed and can't be removed.</summary>
    EndpointFixed,

    /// <summary>A design names a sink that isn't one of the goal's candidates.</summary>
    UnknownSink,
}

public readonly record struct AssemblyResult(AssemblyFailure Failure, PartInstanceId Instance = default)
{
    public bool Succeeded => Failure == AssemblyFailure.None;

    public static AssemblyResult Ok(PartInstanceId instance = default) => new(AssemblyFailure.None, instance);
}
