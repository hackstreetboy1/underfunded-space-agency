using Usa.Simulation.Ports;

namespace Usa.Simulation.Expressions;

internal sealed class CompiledExpression(string source, string[] paths, Node root) : IExpression
{
    public string Source { get; } = source;

    public IReadOnlyList<string> Paths { get; } = paths;

    public StateValue Evaluate(IStateQuery state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return root.Evaluate(state, Source);
    }

    public override string ToString() => Source;
}
