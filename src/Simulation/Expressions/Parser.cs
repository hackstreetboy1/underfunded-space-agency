using Usa.Simulation.Ports;

namespace Usa.Simulation.Expressions;

/// <summary>
/// Recursive-descent parser. Precedence from loosest to tightest: or, and, comparison (not chainable),
/// + -, * /, unary - and not, primary. Kinds that are known at compile time are checked here so most
/// type errors surface at load with a column.
/// </summary>
internal sealed class Parser
{
    private const int MaxDepth = 64;

    private readonly string _source;
    private readonly List<Token> _tokens;
    private readonly List<string> _paths = [];
    private int _position;
    private int _depth;

    public Parser(string source)
    {
        _source = source;
        _tokens = Lexer.Tokenize(source);
    }

    public CompiledExpression Parse()
    {
        if (Current.Kind == TokenKind.End) throw Error(Current, "Expression is empty");
        var root = ParseOr();
        if (Current.Kind != TokenKind.End) throw Error(Current, $"Unexpected '{Current.Text}'");
        return new CompiledExpression(_source, [.. _paths], root);
    }

    private Token Current => _tokens[_position];

    private Token Advance() => _tokens[_position++];

    private Node ParseOr()
    {
        var first = ParseAnd();
        if (Current.Kind != TokenKind.Or) return first;
        var column = Current.Column;
        var operands = new List<Node> { RequireBool(first, "or") };
        while (Current.Kind == TokenKind.Or)
        {
            Advance();
            operands.Add(RequireBool(ParseAnd(), "or"));
        }
        return new AnyNode(column, "or", [.. operands]);
    }

    private Node ParseAnd()
    {
        var first = ParseComparison();
        if (Current.Kind != TokenKind.And) return first;
        var column = Current.Column;
        var operands = new List<Node> { RequireBool(first, "and") };
        while (Current.Kind == TokenKind.And)
        {
            Advance();
            operands.Add(RequireBool(ParseComparison(), "and"));
        }
        return new AllNode(column, "and", [.. operands]);
    }

    private Node ParseComparison()
    {
        var left = ParseAdditive();
        if (ComparisonOf(Current.Kind) is not { } op) return left;

        var token = Advance();
        var right = ParseAdditive();
        if (op is ComparisonOperator.Equal or ComparisonOperator.NotEqual)
        {
            if (left.StaticKind != StateValueKind.None && right.StaticKind != StateValueKind.None && left.StaticKind != right.StaticKind)
            {
                throw Error(token, $"'{token.Text}' compares values of the same kind but got a {KindName(left.StaticKind)} and a {KindName(right.StaticKind)}");
            }
        }
        else
        {
            RequireNumber(left, token.Text);
            RequireNumber(right, token.Text);
        }

        if (ComparisonOf(Current.Kind) is not null)
            throw Error(Current, "Comparisons can't be chained; combine them with 'and'");
        return new ComparisonNode(token.Column, op, token.Text, left, right);
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var token = Advance();
            var right = ParseMultiplicative();
            var op = token.Kind == TokenKind.Plus ? ArithmeticOperator.Add : ArithmeticOperator.Subtract;
            left = new ArithmeticNode(token.Column, op, token.Text, RequireNumber(left, token.Text), RequireNumber(right, token.Text));
        }
        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var token = Advance();
            var right = ParseUnary();
            var op = token.Kind == TokenKind.Star ? ArithmeticOperator.Multiply : ArithmeticOperator.Divide;
            left = new ArithmeticNode(token.Column, op, token.Text, RequireNumber(left, token.Text), RequireNumber(right, token.Text));
        }
        return left;
    }

    private Node ParseUnary()
    {
        if (Current.Kind is not (TokenKind.Minus or TokenKind.Not)) return ParsePrimary();

        var token = Advance();
        Enter(token);
        var operand = ParseUnary();
        _depth--;
        return token.Kind == TokenKind.Minus
            ? new NegateNode(token.Column, RequireNumber(operand, "-"))
            : new NotNode(token.Column, RequireBool(operand, "not"));
    }

    private Node ParsePrimary()
    {
        var token = Advance();
        switch (token.Kind)
        {
            case TokenKind.Number:
                return new ConstantNode(token.Column, StateValue.Of(token.Number));
            case TokenKind.Text:
                return new ConstantNode(token.Column, StateValue.Of(token.Text));
            case TokenKind.True:
                return new ConstantNode(token.Column, StateValue.Of(true));
            case TokenKind.False:
                return new ConstantNode(token.Column, StateValue.Of(false));
            case TokenKind.Path:
                if (!_paths.Contains(token.Text, StringComparer.Ordinal)) _paths.Add(token.Text);
                return new PathNode(token.Column, token.Text);
            case TokenKind.LeftParen:
                Enter(token);
                var inner = ParseOr();
                Expect(TokenKind.RightParen, "Expected ')'");
                _depth--;
                return inner;
            case TokenKind.Identifier:
                return ParseCall(token);
            case TokenKind.End:
                throw Error(token, "Unexpected end of expression");
            default:
                throw Error(token, $"Unexpected '{token.Text}'");
        }
    }

    private Node ParseCall(Token name)
    {
        if (Current.Kind != TokenKind.LeftParen)
            throw Error(name, $"Unknown name '{name.Text}' (state paths start with '@', text uses single quotes)");
        Enter(name);
        Advance();
        var arguments = new List<Node>();
        if (Current.Kind != TokenKind.RightParen)
        {
            arguments.Add(ParseOr());
            while (Current.Kind == TokenKind.Comma)
            {
                Advance();
                arguments.Add(ParseOr());
            }
        }
        Expect(TokenKind.RightParen, "Expected ',' or ')'");
        _depth--;

        switch (name.Text)
        {
            case "any":
            case "all":
                RequireArity(name, arguments.Count, 1, int.MaxValue);
                foreach (var argument in arguments) RequireBool(argument, name.Text);
                return name.Text == "any"
                    ? new AnyNode(name.Column, "any", [.. arguments])
                    : new AllNode(name.Column, "all", [.. arguments]);
            case "abs":
                RequireArity(name, arguments.Count, 1, 1);
                return new AbsNode(name.Column, RequireNumber(arguments[0], "abs"));
            case "min":
            case "max":
                RequireArity(name, arguments.Count, 1, int.MaxValue);
                foreach (var argument in arguments) RequireNumber(argument, name.Text);
                return new ExtremumNode(name.Column, name.Text == "max", [.. arguments]);
            case "clamp":
                RequireArity(name, arguments.Count, 3, 3);
                foreach (var argument in arguments) RequireNumber(argument, "clamp");
                return new ClampNode(name.Column, arguments[0], arguments[1], arguments[2]);
            default:
                throw Error(name, $"Unknown function '{name.Text}'");
        }
    }

    private void RequireArity(Token name, int count, int min, int max)
    {
        if (count >= min && count <= max) return;
        var expected = min == max ? $"exactly {min}" : $"at least {min}";
        throw Error(name, $"'{name.Text}' takes {expected} argument{(min == 1 ? "" : "s")} but got {count}");
    }

    private Node RequireNumber(Node node, string operation) => Require(node, StateValueKind.Number, operation, "numbers");

    private Node RequireBool(Node node, string operation) => Require(node, StateValueKind.Bool, operation, "booleans");

    private Node Require(Node node, StateValueKind kind, string operation, string expected) =>
        node.StaticKind == StateValueKind.None || node.StaticKind == kind
            ? node
            : throw new ExpressionException(_source, node.Column, $"'{operation}' needs {expected} but got a {KindName(node.StaticKind)}");

    private void Expect(TokenKind kind, string message)
    {
        if (Current.Kind != kind) throw Error(Current, message);
        Advance();
    }

    private void Enter(Token token)
    {
        if (++_depth > MaxDepth) throw Error(token, "Expression is nested too deeply");
    }

    private ExpressionException Error(Token token, string message) => new(_source, token.Column, message);

    private static ComparisonOperator? ComparisonOf(TokenKind kind) => kind switch
    {
        TokenKind.Less => ComparisonOperator.Less,
        TokenKind.LessEqual => ComparisonOperator.LessEqual,
        TokenKind.Greater => ComparisonOperator.Greater,
        TokenKind.GreaterEqual => ComparisonOperator.GreaterEqual,
        TokenKind.Equal => ComparisonOperator.Equal,
        TokenKind.NotEqual => ComparisonOperator.NotEqual,
        _ => null,
    };

    private static string KindName(StateValueKind kind) => kind switch
    {
        StateValueKind.Number => "number",
        StateValueKind.Bool => "boolean",
        StateValueKind.Text => "text",
        _ => "value",
    };
}
