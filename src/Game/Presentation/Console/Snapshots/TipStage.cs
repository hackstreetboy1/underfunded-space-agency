namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Tip strength, weakest first (GDD 9.1; mirrors the guidance <c>HintStage</c>).</summary>
public enum TipStage
{
    None = 0,
    Nudge = 1,
    Investigate = 2,
    Connect = 3,
    Recommend = 4,
    Direct = 5,
}
