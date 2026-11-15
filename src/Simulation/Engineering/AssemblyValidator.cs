using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>
/// The workspace's digital test (GDD 8, SCRIPTING 11.8). Every part is a two-ended series element, so the
/// circuit is the chain of links followed from the source port; it either reaches the sink or ends at an open
/// connector. Checks never solve the design; they only say what is wrong. Findings carry names and figures,
/// and the workspace words them (<c>workspace.check*</c> strings).
/// </summary>
public sealed class AssemblyValidator(PartCatalog catalog)
{
    private readonly PartCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public AssemblyReport Validate(AssemblyGraph graph, Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(inventory);

        var circuit = Trace(graph);
        CheckResult[] checks =
        [
            Continuity(graph, circuit),
            Connectors(graph),
            circuit.Complete ? Protection(graph.Goal, circuit) : Skipped(CheckKind.Protection),
            circuit.Complete ? Length(graph.Goal, circuit) : Skipped(CheckKind.Length),
            ShortToGround(graph, circuit),
            InventoryCheck(graph, inventory),
        ];

        RepairTaskSpec? task = null;
        if (Array.TrueForAll(checks, c => c.Passed)) task = BuildTask(graph, circuit);
        return new AssemblyReport(checks, task);
    }

    private sealed record Circuit(List<PlacedPart> Path, bool SourceLinked, bool Complete, PortRef? OpenEnd);

    private static Circuit Trace(AssemblyGraph graph)
    {
        var path = new List<PlacedPart>();
        var next = graph.LinkedTo(PortRef.Source);
        var sourceLinked = next is not null;
        while (next is { } entry)
        {
            if (entry.Instance == PartInstanceId.Sink) return new Circuit(path, sourceLinked, true, null);
            if (!graph.TryGetPart(entry.Instance, out var placed) || path.Contains(placed)) break;
            path.Add(placed);
            var exit = new PortRef(entry.Instance, 1 - entry.Port);
            next = graph.LinkedTo(exit);
            if (next is null) return new Circuit(path, sourceLinked, false, exit);
        }
        return new Circuit(path, sourceLinked, false, null);
    }

    private CheckResult Continuity(AssemblyGraph graph, Circuit circuit)
    {
        var source = Arg("source", graph.Goal.Source.Label);
        var sink = Arg("sink", graph.Sink.Port.Label);
        if (!circuit.SourceLinked)
            return Fail(CheckKind.Continuity, CheckReason.SourceNotConnected, new CheckFinding(CheckNote.SourceNotConnected, [source]));
        if (!circuit.Complete)
        {
            var finding = circuit.OpenEnd is { } open
                ? new CheckFinding(CheckNote.OpenEnd,
                    [source, Arg("connector", ConnectorName(graph, open)), Arg("part", graph.LabelOf(open.Instance)), sink])
                : new CheckFinding(CheckNote.OpenPartway, [source, sink]);
            return Fail(CheckKind.Continuity, CheckReason.OpenCircuit, finding);
        }
        return Pass(CheckKind.Continuity, new CheckFinding(CheckNote.ContinuityOk, [source, sink]));
    }

    private CheckResult Connectors(AssemblyGraph graph)
    {
        var problems = new List<CheckFinding>();
        foreach (var link in graph.Links())
        {
            if (graph.ConnectorAt(link.A) == graph.ConnectorAt(link.B)) continue;
            problems.Add(new CheckFinding(CheckNote.ConnectorMismatch,
            [
                Arg("connector", ConnectorName(graph, link.A)), Reference("at", graph, link.A.Instance),
                Arg("otherConnector", ConnectorName(graph, link.B)), Reference("other", graph, link.B.Instance),
            ]));
        }
        if (problems.Count == 0) return Pass(CheckKind.ConnectorCompatibility, new CheckFinding(CheckNote.ConnectorsMate));
        problems.Add(new CheckFinding(CheckNote.AdapterHint));
        return new CheckResult(CheckKind.ConnectorCompatibility, CheckStatus.Failed, CheckReason.ConnectorMismatch, problems);
    }

    /// <summary>
    /// The weakest protective part must trip above the load (a fuse at exactly the load current trips in use)
    /// and at or below the weakest conductor actually in the circuit: the goal's wiring or any placed part.
    /// Findings name parts by <see cref="PartDefinition.ShortName"/>, since the rating is printed beside the name.
    /// </summary>
    private static CheckResult Protection(RepairGoal goal, Circuit circuit)
    {
        var load = Arg("load", EngineeringFormat.Amps(goal.LoadCurrentA));
        PlacedPart? lowest = null;
        PlacedPart? weakestConductor = null;
        foreach (var placed in circuit.Path)
        {
            var part = placed.Part;
            if (part.IsProtection)
            {
                if (part.CurrentRatingA <= goal.LoadCurrentA)
                {
                    return Fail(CheckKind.Protection, CheckReason.ProtectionUnderrated,
                        new CheckFinding(CheckNote.ProtectionUnderrated, [Arg("part", part.ShortName), Rating(part), load]));
                }
                if (lowest is null || part.CurrentRatingA < lowest.Part.CurrentRatingA) lowest = placed;
            }
            else if (part.CurrentRatingA < goal.LoadCurrentA)
            {
                return Fail(CheckKind.Protection, CheckReason.PartUnderrated,
                    new CheckFinding(CheckNote.PartUnderrated, [Arg("part", part.ShortName), Rating(part), load]));
            }
            else if (weakestConductor is null || part.CurrentRatingA < weakestConductor.Part.CurrentRatingA)
            {
                weakestConductor = placed;
            }
        }

        var limitA = goal.WireRatingA;
        PartDefinition? limitPart = null;
        if (weakestConductor is { } conductor && conductor.Part.CurrentRatingA < limitA)
        {
            limitA = conductor.Part.CurrentRatingA;
            limitPart = conductor.Part;
        }
        var limit = Arg("limit", EngineeringFormat.Amps(limitA));

        if (lowest is null)
            return Fail(CheckKind.Protection, CheckReason.NoProtection, new CheckFinding(CheckNote.NoProtection, [load, limit]));

        var protector = lowest.Part;
        var name = Arg("part", protector.ShortName);
        if (protector.CurrentRatingA > limitA)
        {
            return Fail(CheckKind.Protection, CheckReason.ProtectionOverrated, limitPart is null
                ? new CheckFinding(CheckNote.ProtectionOverrated, [name, Rating(protector), limit])
                : new CheckFinding(CheckNote.ProtectionOverratedByPart, [name, Rating(protector), limit, Arg("conductor", limitPart.ShortName)]));
        }
        return Pass(CheckKind.Protection, limitPart is null
            ? new CheckFinding(CheckNote.ProtectionOk, [name, Rating(protector), load, limit])
            : new CheckFinding(CheckNote.ProtectionOkByPart, [name, Rating(protector), load, limit, Arg("conductor", limitPart.ShortName)]));
    }

    private static CheckResult Length(RepairGoal goal, Circuit circuit)
    {
        var totalM = 0.0;
        foreach (var placed in circuit.Path) totalM += placed.Part.LengthM;
        CheckArg[] args = [Arg("length", EngineeringFormat.Metres(totalM)), Arg("minimum", EngineeringFormat.Metres(goal.MinLengthM))];
        return totalM >= goal.MinLengthM
            ? Pass(CheckKind.Length, new CheckFinding(CheckNote.LengthOk, args))
            : Fail(CheckKind.Length, CheckReason.TooShort, new CheckFinding(CheckNote.TooShort, args));
    }

    private CheckResult ShortToGround(AssemblyGraph graph, Circuit circuit)
    {
        if (circuit.OpenEnd is { } open)
        {
            return Fail(CheckKind.ShortToGround, CheckReason.LiveOpenPort, new CheckFinding(CheckNote.LiveOpenPort,
                [Arg("connector", ConnectorName(graph, open)), Arg("part", graph.LabelOf(open.Instance))]));
        }

        var stray = new List<string>();
        foreach (var placed in graph.Parts)
        {
            if (!circuit.Path.Contains(placed)) stray.Add(placed.Part.Name);
        }
        return stray.Count > 0
            ? Fail(CheckKind.ShortToGround, CheckReason.StrayPart,
                new CheckFinding(CheckNote.StrayParts, [Arg("parts", string.Join(", ", stray))]))
            : Pass(CheckKind.ShortToGround, new CheckFinding(CheckNote.NoLiveOpenPort));
    }

    private CheckResult InventoryCheck(AssemblyGraph graph, Inventory inventory)
    {
        var problems = new List<CheckFinding>();
        foreach (var (part, needed) in CountParts(graph.Parts))
        {
            var available = inventory.Available(part);
            if (needed <= available) continue;
            var count = Arg("count", EngineeringFormat.Count(needed));
            var name = Arg("part", _catalog.Get(part).Name);
            problems.Add(available switch
            {
                0 => new CheckFinding(CheckNote.NoneOnBoard, [count, name]),
                1 => new CheckFinding(CheckNote.OneOnBoard, [count, name]),
                _ => new CheckFinding(CheckNote.SomeOnBoard, [count, name, Arg("available", EngineeringFormat.Count(available))]),
            });
        }
        return problems.Count == 0
            ? Pass(CheckKind.Inventory, new CheckFinding(CheckNote.AllPartsOnBoard))
            : new CheckResult(CheckKind.Inventory, CheckStatus.Failed, CheckReason.NotEnoughParts, problems);
    }

    private RepairTaskSpec BuildTask(AssemblyGraph graph, Circuit circuit)
    {
        var goal = graph.Goal;
        var parameters = goal.Task;
        var partCount = circuit.Path.Count;
        var used = CountParts(circuit.Path);
        var consumedOnFailure = new List<PartQuantity>();
        foreach (var quantity in used)
        {
            if (_catalog.Get(quantity.Part).ConsumedOnFailure) consumedOnFailure.Add(quantity);
        }

        var complexity = Math.Min(parameters.MaxComplexity, parameters.BaseComplexity + (partCount / parameters.PartsPerComplexityStep));
        return new RepairTaskSpec(
            goal.Id,
            parameters.TaskId,
            parameters.Skill,
            goal.Source.Component,
            graph.Sink.Definition.Component,
            graph.Sink.Definition.Repairs,
            parameters.BaseDurationS + (parameters.DurationPerPartS * partCount),
            complexity,
            partCount,
            used,
            consumedOnFailure,
            graph.ToDesign());
    }

    /// <summary>Counts per part id, sorted by id.</summary>
    private static List<PartQuantity> CountParts(IEnumerable<PlacedPart> parts) =>
        [.. parts.GroupBy(p => p.Part.Id).OrderBy(g => g.Key).Select(g => new PartQuantity(g.Key, g.Count()))];

    private string ConnectorName(AssemblyGraph graph, PortRef port) => _catalog.ConnectorName(graph.ConnectorAt(port));

    private static CheckArg Arg(string name, string value) => new(name, value);

    private static CheckArg Rating(PartDefinition part) => new("rating", EngineeringFormat.Amps(part.CurrentRatingA));

    private static CheckArg Reference(string name, AssemblyGraph graph, PartInstanceId instance) =>
        new(name, graph.LabelOf(instance), IsPlacedPart: !instance.IsEndpoint);

    private static CheckResult Pass(CheckKind check, CheckFinding finding) => new(check, CheckStatus.Passed, CheckReason.None, [finding]);

    private static CheckResult Fail(CheckKind check, CheckReason reason, CheckFinding finding) => new(check, CheckStatus.Failed, reason, [finding]);

    private static CheckResult Skipped(CheckKind check) =>
        new(check, CheckStatus.Skipped, CheckReason.NeedsContinuity, [new CheckFinding(CheckNote.NeedsContinuity)]);
}
