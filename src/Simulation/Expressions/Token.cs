namespace Usa.Simulation.Expressions;

internal enum TokenKind
{
    End,
    Number,
    Text,
    Path,
    True,
    False,
    And,
    Or,
    Not,
    Identifier,
    LeftParen,
    RightParen,
    Comma,
    Plus,
    Minus,
    Star,
    Slash,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Equal,
    NotEqual,
}

/// <param name="Column">1-based column of the token's first character.</param>
/// <param name="Text">Path (without '@'), text literal contents, identifier or operator spelling.</param>
internal readonly record struct Token(TokenKind Kind, int Column, string Text, double Number);
