using Usa.Simulation.Ports;

namespace Usa.Simulation.Expressions;

/// <summary>
/// AST node. <see cref="StaticKind"/> is the result kind known at compile time, or
/// <see cref="StateValueKind.None"/> when it depends on state (paths).
/// </summary>
internal abstract class Node(int column, StateValueKind staticKind)
{
    public int Column { get; } = column;

    public StateValueKind StaticKind { get; } = staticKind;

    public abstract StateValue Evaluate(IStateQuery state, string source);

    protected static double NumberOf(StateValue value, Node operand, string operation, string source) =>
        value.Kind == StateValueKind.Number ? value.AsNumber() : throw Mismatch(value, operand, operation, "numbers", source);

    protected static bool BoolOf(StateValue value, Node operand, string operation, string source) =>
        value.Kind == StateValueKind.Bool ? value.AsBool() : throw Mismatch(value, operand, operation, "booleans", source);

    private static ExpressionEvaluationException Mismatch(StateValue value, Node operand, string operation, string expected, string source) =>
        new(source, operand.Column, $"'{operation}' needs {expected} but got {Describe(value)}");

    internal static string Describe(StateValue value) => value.Kind switch
    {
        StateValueKind.Number => $"the number {value}",
        StateValueKind.Bool => $"the boolean {value}",
        StateValueKind.Text => $"the text {value}",
        _ => "no value",
    };
}

internal sealed class ConstantNode(int column, StateValue value) : Node(column, value.Kind)
{
    public override StateValue Evaluate(IStateQuery state, string source) => value;
}

internal sealed class PathNode(int column, string path) : Node(column, StateValueKind.None)
{
    public override StateValue Evaluate(IStateQuery state, string source) =>
        state.TryGet(path, out var value)
            ? value
            : throw new ExpressionEvaluationException(source, Column, $"Unknown state path '{path}'");
}

internal sealed class NegateNode(int column, Node operand) : Node(column, StateValueKind.Number)
{
    public override StateValue Evaluate(IStateQuery state, string source) =>
        StateValue.Of(-NumberOf(operand.Evaluate(state, source), operand, "-", source));
}

internal sealed class NotNode(int column, Node operand) : Node(column, StateValueKind.Bool)
{
    public override StateValue Evaluate(IStateQuery state, string source) =>
        StateValue.Of(!BoolOf(operand.Evaluate(state, source), operand, "not", source));
}

internal enum ArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
}

/// <remarks>Division follows IEEE 754: dividing by zero gives an infinity or NaN rather than throwing.</remarks>
internal sealed class ArithmeticNode(int column, ArithmeticOperator op, string spelling, Node left, Node right)
    : Node(column, StateValueKind.Number)
{
    public override StateValue Evaluate(IStateQuery state, string source)
    {
        var a = NumberOf(left.Evaluate(state, source), left, spelling, source);
        var b = NumberOf(right.Evaluate(state, source), right, spelling, source);
        return StateValue.Of(op switch
        {
            ArithmeticOperator.Add => a + b,
            ArithmeticOperator.Subtract => a - b,
            ArithmeticOperator.Multiply => a * b,
            _ => a / b,
        });
    }
}

internal enum ComparisonOperator
{
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Equal,
    NotEqual,
}

internal sealed class ComparisonNode(int column, ComparisonOperator op, string spelling, Node left, Node right)
    : Node(column, StateValueKind.Bool)
{
    public override StateValue Evaluate(IStateQuery state, string source)
    {
        var a = left.Evaluate(state, source);
        var b = right.Evaluate(state, source);
        if (op is ComparisonOperator.Equal or ComparisonOperator.NotEqual)
        {
            if (a.Kind != b.Kind || a.Kind == StateValueKind.None)
            {
                throw new ExpressionEvaluationException(source, Column,
                    $"'{spelling}' compares values of the same kind but got {Describe(a)} and {Describe(b)}");
            }
            var equal = a.Kind == StateValueKind.Number ? a.AsNumber() == b.AsNumber() : a.Equals(b);
            return StateValue.Of(op == ComparisonOperator.Equal ? equal : !equal);
        }

        var x = NumberOf(a, left, spelling, source);
        var y = NumberOf(b, right, spelling, source);
        return StateValue.Of(op switch
        {
            ComparisonOperator.Less => x < y,
            ComparisonOperator.LessEqual => x <= y,
            ComparisonOperator.Greater => x > y,
            _ => x >= y,
        });
    }
}

/// <summary>Short-circuit 'and' / all(...): stops at the first false operand.</summary>
internal sealed class AllNode(int column, string spelling, Node[] operands) : Node(column, StateValueKind.Bool)
{
    public override StateValue Evaluate(IStateQuery state, string source)
    {
        for (var i = 0; i < operands.Length; i++)
        {
            if (!BoolOf(operands[i].Evaluate(state, source), operands[i], spelling, source)) return StateValue.Of(false);
        }
        return StateValue.Of(true);
    }
}

/// <summary>Short-circuit 'or' / any(...): stops at the first true operand.</summary>
internal sealed class AnyNode(int column, string spelling, Node[] operands) : Node(column, StateValueKind.Bool)
{
    public override StateValue Evaluate(IStateQuery state, string source)
    {
        for (var i = 0; i < operands.Length; i++)
        {
            if (BoolOf(operands[i].Evaluate(state, source), operands[i], spelling, source)) return StateValue.Of(true);
        }
        return StateValue.Of(false);
    }
}

internal sealed class AbsNode(int column, Node operand) : Node(column, StateValueKind.Number)
{
    public override StateValue Evaluate(IStateQuery state, string source) =>
        StateValue.Of(Math.Abs(NumberOf(operand.Evaluate(state, source), operand, "abs", source)));
}

internal sealed class ExtremumNode(int column, bool maximum, Node[] operands) : Node(column, StateValueKind.Number)
{
    public override StateValue Evaluate(IStateQuery state, string source)
    {
        var spelling = maximum ? "max" : "min";
        var result = NumberOf(operands[0].Evaluate(state, source), operands[0], spelling, source);
        for (var i = 1; i < operands.Length; i++)
        {
            var value = NumberOf(operands[i].Evaluate(state, source), operands[i], spelling, source);
            result = maximum ? Math.Max(result, value) : Math.Min(result, value);
        }
        return StateValue.Of(result);
    }
}

internal sealed class ClampNode(int column, Node value, Node low, Node high) : Node(column, StateValueKind.Number)
{
    public override StateValue Evaluate(IStateQuery state, string source)
    {
        var x = NumberOf(value.Evaluate(state, source), value, "clamp", source);
        var lo = NumberOf(low.Evaluate(state, source), low, "clamp", source);
        var hi = NumberOf(high.Evaluate(state, source), high, "clamp", source);
        if (lo > hi)
            throw new ExpressionEvaluationException(source, Column, $"'clamp' lower bound {StateValue.Of(lo)} is above upper bound {StateValue.Of(hi)}");
        return StateValue.Of(x < lo ? lo : x > hi ? hi : x);
    }
}
