using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Simulation.Session;

/// <summary>
/// Session-level commands: <c>stop_task</c> stops the crew slot (refused silently for non-interruptible tasks; the runner keeps
/// them), <c>acknowledge_alarms</c> acknowledges the annunciator, <c>status_report</c> publishes what the reporting member knows,
/// <c>register_repair_design</c> registers a workspace design (<see cref="RepairDesignCodec"/>) with <see cref="RepairWorkspace"/>,
/// <c>telemetry_dump</c> records one precise sensor reading of each telemetry fact (<see cref="InstrumentTelemetry"/>; a failed
/// or unpowered sensor gives none). The ground reads the dump back over the radio, so every living crew member is told each
/// reading (review SIM-2 S11) and may quote it.
/// </summary>
/// <param name="crew">Every crew member, ordinal by id: who hears a telemetry dump.</param>
internal sealed class MetaCommandExecutor(
    CrewTaskRunner runner, AlarmMonitor alarms, KnowledgeBase knowledge, RepairWorkspace repairs, EventBus events,
    FactCatalog facts, InstrumentTelemetry telemetry, IReadOnlyList<CrewId> crew) : IMetaCommandExecutor
{
    public string? Check(MetaCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind != MetaCommandKind.RegisterRepairDesign) return null;
        var (assembly, design) = Design(command);
        return repairs.Check(assembly, design);
    }

    public void Execute(MetaCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command.Kind)
        {
            case MetaCommandKind.StopTask:
                runner.Stop(command.Now, TaskStopReason.Player);
                break;
            case MetaCommandKind.AcknowledgeAlarms:
                alarms.AcknowledgeAll(command.Now);
                break;
            case MetaCommandKind.StatusReport when command.Crew is { } crew:
                events.Publish(new StatusReportReady(command.Now, command.Origin, crew, knowledge.FactsKnownBy(crew)));
                break;
            case MetaCommandKind.RegisterRepairDesign:
                var (assembly, design) = Design(command);
                if (design is not null && assembly.Length > 0) repairs.Register(assembly, design, command.Now);
                break;
            case MetaCommandKind.TelemetryDump:
                DumpTelemetry(command);
                break;
        }
    }

    private void DumpTelemetry(MetaCommand command)
    {
        var only = command.Arguments.TryGetValue(TelemetryDumpArgs.Fact, out var arg) && arg.Kind == CommandArgKind.Text ? arg.AsText() : null;
        foreach (var fact in facts.Facts)
        {
            if (!fact.Allows(ObservationMethod.Telemetry) || (only is not null && fact.Id.Value != only)) continue;
            if (telemetry.ReadFact(fact) is { } reading) knowledge.ReportTelemetryReading(fact, reading, command.Now, crew);
        }
    }

    private static (string Assembly, AssemblyDesign? Design) Design(MetaCommand command)
    {
        var args = command.Arguments;
        var assembly = args.TryGetValue(RepairDesignArgs.Assembly, out var id) && id.Kind == CommandArgKind.Text ? id.AsText() : "";
        var design = args.TryGetValue(RepairDesignArgs.Design, out var json) && json.Kind == CommandArgKind.Text
            ? RepairDesignCodec.TryParse(json.AsText())
            : null;
        return (assembly, design);
    }
}
