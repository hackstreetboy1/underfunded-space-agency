using Usa.Simulation.Ports;

namespace Usa.Simulation.Engineering;

/// <summary>A repair the engineering workspace can design for (SCRIPTING 11.8), with compiled sink conditions.</summary>
public sealed class RepairGoal
{
    internal RepairGoal(RepairGoalDefinition definition, IReadOnlyList<RepairSink> sinks)
    {
        Definition = definition;
        SinkCandidates = sinks;
    }

    public RepairGoalDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public RepairPortDefinition Source => Definition.Source;

    public IReadOnlyList<RepairSink> SinkCandidates { get; }

    public double LoadCurrentA => Definition.LoadCurrentA;

    public double WireRatingA => Definition.WireRatingA;

    public double MinLengthM => Definition.MinLengthM;

    public RepairTaskParameters Task => Definition.Task;

    /// <summary>The first sink candidate whose condition holds, or null when nothing needs repairing.</summary>
    public RepairSink? ResolveSink(IStateQuery state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var sink in SinkCandidates)
        {
            if (sink.When.IsTrue(state)) return sink;
        }
        return null;
    }
}

/// <summary>A sink candidate with its compiled selection condition.</summary>
public sealed class RepairSink(RepairSinkDefinition definition, IExpression when)
{
    public RepairSinkDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    public IExpression When { get; } = when ?? throw new ArgumentNullException(nameof(when));

    public RepairPortDefinition Port { get; } = new(definition.Component, definition.Label, definition.Connector);
}
