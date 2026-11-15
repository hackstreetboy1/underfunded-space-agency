using Godot;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Console;

/// <summary>
/// Resolves the view models' colour token keys (<c>ColourTokens</c>) in one token mode, for the few colours the theme
/// has no type variation for (status glyphs, stale values, log rows, the alarm band).
/// </summary>
public sealed class ConsolePalette
{
    /// <summary>Token keys the view models' <c>ColourTokens</c> do not list.</summary>
    public const string Bg0 = "bg.0";

    public const string Bg3 = "bg.3";

    public ConsolePalette(UiTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        Tokens = tokens;
    }

    public static ConsolePalette Default { get; } = new(UiTokens.Default);

    public static ConsolePalette HighContrast { get; } = new(UiTokens.HighContrast);

    public UiTokenSet Tokens { get; }

    /// <summary>The token's colour; <c>text.hi</c> for null or unknown keys.</summary>
    public Color Of(string? tokenKey) =>
        tokenKey is not null && Tokens.FindColor(tokenKey) is { } colour ? colour : Tokens.ColorTextHi;
}
