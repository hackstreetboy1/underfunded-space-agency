using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;

namespace Usa.Game.Guidance;

/// <summary>
/// Everything the console tip card shows for the current tip (UI.md 3.3): maps one-to-one onto
/// <c>GuidanceTipSnapshot</c> (<see cref="TipId"/>, stage, text, issue time, fact chips).
/// </summary>
/// <param name="TipId">The problem id; together with <paramref name="Stage"/> it identifies one tip.</param>
/// <param name="Text">The authored text, or its validated rephrasing once one arrives.</param>
public sealed record TipCardInput(string TipId, HintStage Stage, string Text, MissionTime IssuedAt, IReadOnlyList<TipFactChip> Facts)
{
    public const int PipCount = 5;

    /// <summary>True when <see cref="Text"/> is a validated rephrasing rather than the authored wording.</summary>
    public bool IsRephrased { get; init; }

    /// <summary>The player asked for this stage with "Stronger hint".</summary>
    public bool Requested { get; init; }

    public int FilledPips => Math.Clamp((int)Stage, 0, PipCount);

    /// <summary>"Stronger hint" can still escalate (not at the direct solution).</summary>
    public bool CanStrengthen => Stage < HintStage.Direct;
}

/// <summary>An inline fact chip. The console derives its badge from <paramref name="Knowledge"/> and its panel from <paramref name="FactKey"/>.</summary>
public sealed record TipFactChip(string FactKey, string Label, ReadoutKnowledge Knowledge);
