using System.Globalization;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Expressions;

/// <summary>Splits expression source into tokens. Paths are '@' followed greedily by [a-z0-9_/.-]+ (lower case only, as ids are).</summary>
internal static class Lexer
{
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            var column = i + 1;
            if (IsDigit(c))
            {
                i = ScanNumber(source, i);
                var number = double.Parse(source.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                tokens.Add(new Token(TokenKind.Number, column, source[start..i], number));
            }
            else if (c == '@')
            {
                i++;
                while (i < source.Length && IsPathChar(source[i])) i++;
                if (i < source.Length && source[i] is >= 'A' and <= 'Z')
                    throw new ExpressionException(source, i + 1, "State paths are lower-case snake_case");
                if (i == start + 1) throw new ExpressionException(source, column, "Expected a state path after '@'");
                tokens.Add(new Token(TokenKind.Path, column, source[(start + 1)..i], 0));
            }
            else if (c == '\'')
            {
                var close = source.IndexOf('\'', i + 1);
                if (close < 0) throw new ExpressionException(source, column, "Unterminated text literal");
                tokens.Add(new Token(TokenKind.Text, column, source[(i + 1)..close], 0));
                i = close + 1;
            }
            else if (IsIdentifierStart(c))
            {
                while (i < source.Length && IsIdentifierChar(source[i])) i++;
                var word = source[start..i];
                tokens.Add(new Token(KeywordKind(word), column, word, 0));
            }
            else
            {
                var (kind, length) = ScanOperator(source, i, column);
                tokens.Add(new Token(kind, column, source.Substring(i, length), 0));
                i += length;
            }
        }
        tokens.Add(new Token(TokenKind.End, source.Length + 1, "", 0));
        return tokens;
    }

    private static int ScanNumber(string source, int i)
    {
        while (i < source.Length && IsDigit(source[i])) i++;
        if (i + 1 < source.Length && source[i] == '.' && IsDigit(source[i + 1]))
        {
            i++;
            while (i < source.Length && IsDigit(source[i])) i++;
        }
        if (i < source.Length && source[i] is 'e' or 'E')
        {
            var exponent = i + 1;
            if (exponent < source.Length && source[exponent] is '+' or '-') exponent++;
            if (exponent < source.Length && IsDigit(source[exponent]))
            {
                i = exponent;
                while (i < source.Length && IsDigit(source[i])) i++;
            }
        }
        return i;
    }

    private static (TokenKind Kind, int Length) ScanOperator(string source, int i, int column)
    {
        var next = i + 1 < source.Length ? source[i + 1] : '\0';
        return source[i] switch
        {
            '(' => (TokenKind.LeftParen, 1),
            ')' => (TokenKind.RightParen, 1),
            ',' => (TokenKind.Comma, 1),
            '+' => (TokenKind.Plus, 1),
            '-' => (TokenKind.Minus, 1),
            '*' => (TokenKind.Star, 1),
            '/' => (TokenKind.Slash, 1),
            '<' => next == '=' ? (TokenKind.LessEqual, 2) : (TokenKind.Less, 1),
            '>' => next == '=' ? (TokenKind.GreaterEqual, 2) : (TokenKind.Greater, 1),
            '=' when next == '=' => (TokenKind.Equal, 2),
            '!' when next == '=' => (TokenKind.NotEqual, 2),
            '=' => throw new ExpressionException(source, column, "Use '==' to compare"),
            '!' => throw new ExpressionException(source, column, "Use 'not' to negate or '!=' to compare"),
            '"' => throw new ExpressionException(source, column, "Text literals use single quotes"),
            _ => throw new ExpressionException(source, column, $"Unexpected character '{source[i]}'"),
        };
    }

    private static TokenKind KeywordKind(string word) => word switch
    {
        "true" => TokenKind.True,
        "false" => TokenKind.False,
        "and" => TokenKind.And,
        "or" => TokenKind.Or,
        "not" => TokenKind.Not,
        _ => TokenKind.Identifier,
    };

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsIdentifierStart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

    private static bool IsIdentifierChar(char c) => IsIdentifierStart(c) || IsDigit(c);

    // Same character set as Kernel.IdRules, so a path the ids could never produce fails here with a clear message.
    private static bool IsPathChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '/' or '.' or '-';
}
