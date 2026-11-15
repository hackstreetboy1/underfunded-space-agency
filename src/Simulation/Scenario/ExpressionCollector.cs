using Usa.Simulation.Data;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Scenario;

/// <param name="IsCondition">The runtime reads it as a boolean (<c>when</c>, <c>complete</c>, <c>runWhile</c>, …).</param>
internal sealed record CollectedExpression(string File, string JsonPath, IExpression Expression, bool IsNamedCondition, bool IsCondition);

/// <summary>Compiles expression sources, reporting failures as definition errors and remembering every result for path checks.</summary>
internal sealed class ExpressionCollector(IExpressionCompiler compiler, DefinitionErrors errors)
{
    private readonly List<CollectedExpression> _collected = [];

    public IReadOnlyList<CollectedExpression> Collected => _collected;

    /// <summary>Compiles a value expression (an effect value).</summary>
    public IExpression Compile(string file, string jsonPath, string? source) =>
        Collect(file, jsonPath, source, isNamedCondition: false, isCondition: false);

    /// <summary>Compiles an expression the runtime evaluates as a boolean; the scenario loader kind-checks it against the composed state.</summary>
    public IExpression CompileCondition(string file, string jsonPath, string? source, bool isNamedCondition = false) =>
        Collect(file, jsonPath, source, isNamedCondition, isCondition: true);

    private IExpression Collect(string file, string jsonPath, string? source, bool isNamedCondition, bool isCondition)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            errors.Add(file, jsonPath, "Expression is empty.");
            return new InvalidExpression(source ?? "");
        }
        try
        {
            var expression = compiler.Compile(source);
            _collected.Add(new CollectedExpression(file, jsonPath, expression, isNamedCondition, isCondition));
            return expression;
        }
        catch (ExpressionException ex)
        {
            errors.Add(file, jsonPath, ex.Message);
            return new InvalidExpression(source);
        }
    }

    /// <summary>Stands in for an expression that failed to compile; the load always throws before it can run.</summary>
    private sealed class InvalidExpression(string source) : IExpression
    {
        public string Source { get; } = source;

        public IReadOnlyList<string> Paths { get; } = [];

        public StateValue Evaluate(IStateQuery state) => throw new InvalidOperationException($"Expression \"{Source}\" failed to compile.");
    }
}
