namespace Usa.Game.Presentation.Console.Status;

/// <summary>Colour token keys from UI.md 2.1, without the <c>color.</c> prefix of tokens.json;
/// <c>UiTokenSet.FindColor</c> resolves them in either mode (default or high-contrast).</summary>
public static class ColourTokens
{
    public const string Bg2 = "bg.2";
    public const string TextHi = "text.hi";
    public const string TextMid = "text.mid";
    public const string TextLo = "text.lo";
    public const string TextOff = "text.off";
    public const string Accent = "accent";
    public const string Info = "info";
    public const string Ok = "ok";
    public const string Caution = "caution";
    public const string Critical = "critical";
    public const string Conflict = "conflict";
    public const string Unknown = "unknown";
    public const string Tape = "tape";
}
