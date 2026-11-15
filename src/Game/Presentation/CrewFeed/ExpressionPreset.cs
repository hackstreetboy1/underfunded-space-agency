namespace Usa.Game.Presentation.CrewFeed;

/// <summary>Expression presets of <c>data/crew/expressions.json</c> (MODELING 4.4).</summary>
public enum ExpressionPreset
{
    Neutral,
    Focused,
    Concerned,
    Strained,
    Alarmed,
    Relieved,
    Impaired,
}

public static class ExpressionPresets
{
    public const int Count = 7;

    /// <summary>Preset names as written in the data file, indexed by <see cref="ExpressionPreset"/>.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["neutral", "focused", "concerned", "strained", "alarmed", "relieved", "impaired"];

    public static string Name(ExpressionPreset preset) => Names[(int)preset];
}
