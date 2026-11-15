using System.Globalization;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>Built-in expressions for required parts, so part checks need no expression compiler.</summary>
internal static class PartExpressions
{
    public static string CountPath(PartId part) => StatePaths.Inventory + part.Value + ".count";

    /// <summary>Count minus reserved: parts set aside for a registered repair are not free for other tasks.</summary>
    public static string AvailablePath(PartId part) => StatePaths.Inventory + part.Value + ".available";

    public static BoundPrecondition Available(TaskPartRequirement requirement) =>
        new(new AtLeastExpression(AvailablePath(requirement.Part), requirement.Count), "missing_part");

    public static EffectDefinition Consume(TaskPartRequirement requirement) =>
        new(CountPath(requirement.Part), EffectOperation.Add, new ConstantExpression(-requirement.Count));

    private sealed class AtLeastExpression(string path, double minimum) : IExpression
    {
        public string Source { get; } = string.Create(CultureInfo.InvariantCulture, $"@{path} >= {minimum}");

        public IReadOnlyList<string> Paths { get; } = [path];

        public StateValue Evaluate(IStateQuery state) => state.GetNumber(path) >= minimum;
    }

    private sealed class ConstantExpression(double value) : IExpression
    {
        public string Source { get; } = value.ToString("R", CultureInfo.InvariantCulture);

        public IReadOnlyList<string> Paths { get; } = [];

        public StateValue Evaluate(IStateQuery state) => value;
    }
}
