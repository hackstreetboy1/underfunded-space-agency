namespace Usa.Simulation.Commands.Catalog;

public enum MetaCommandKind
{
    StopTask,
    AcknowledgeAlarms,
    StatusReport,

    /// <summary>
    /// Records a workspace repair design so the command log can replay it. Takes string parameters
    /// <see cref="RepairDesignArgs.Assembly"/> and <see cref="RepairDesignArgs.Design"/>; workspace and replay sources only.
    /// </summary>
    RegisterRepairDesign,

    /// <summary>
    /// A ground telemetry dump: one precise reading, through its sensor, of every fact that allows the telemetry method, or of
    /// the fact named by the optional enum parameter <see cref="TelemetryDumpArgs.Fact"/>. A failed or unpowered sensor gives no
    /// reading. Needs no crew member.
    /// </summary>
    TelemetryDump,
}

/// <summary>Parameter names of a <see cref="MetaCommandKind.TelemetryDump"/> action.</summary>
public static class TelemetryDumpArgs
{
    /// <summary>Optional: the one fact to dump.</summary>
    public const string Fact = "fact";
}

/// <summary>Parameter names of a <see cref="MetaCommandKind.RegisterRepairDesign"/> action.</summary>
public static class RepairDesignArgs
{
    /// <summary>The assembly id the design is registered under (the id <c>install_repair</c> later names).</summary>
    public const string Assembly = "assembly";

    /// <summary>The Engineering <c>AssemblyDesign</c> serialized as JSON; the session deserializes and validates it.</summary>
    public const string Design = "design";
}
