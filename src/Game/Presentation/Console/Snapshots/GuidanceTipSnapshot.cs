using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>The current flight-director tip (GDD 9.1).</summary>
/// <param name="TipId">Stable per problem; together with <paramref name="Stage"/> it identifies one tip.</param>
public sealed record GuidanceTipSnapshot(string TipId, TipStage Stage, string Text, MissionTime IssuedAt)
{
    public IReadOnlyList<TipFactSnapshot> Facts { get; init; } = [];
}

/// <summary>A fact referenced by a tip, shown as an inline chip, e.g. <c>OAMS 212 kg · 06:58</c>.</summary>
public sealed record TipFactSnapshot(string Label)
{
    public string? FactKey { get; init; }

    /// <summary>Panel highlighted when the chip is clicked.</summary>
    public SystemsTab? Tab { get; init; }

    public ReadoutKnowledge Knowledge { get; init; } = ReadoutKnowledge.Known;
}
