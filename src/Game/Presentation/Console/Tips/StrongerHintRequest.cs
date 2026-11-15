using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Presentation.Console.Tips;

/// <summary>The player asked for a stronger hint (allowed at any time, GDD 9.1).</summary>
/// <param name="TipId">The tip on screen, or null when none is.</param>
public sealed record StrongerHintRequest(string? TipId, TipStage CurrentStage);
