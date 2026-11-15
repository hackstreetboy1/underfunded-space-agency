using System.Globalization;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>
/// How a diagram node is drawn, in the status language (UI.md 2.2: colour + shape + word) with its knowledge
/// state (2.3). Unknown and suspected nodes have dashed outlines; confirmed failures get a thick border. A stale
/// confirmation keeps its look, dimmed, with a <c>STALE 12m</c> badge.
/// </summary>
/// <param name="ColorToken">UI.md colour token: <c>ok</c>, <c>caution</c>, <c>critical</c>, <c>unknown</c>.</param>
/// <param name="ShapeKey">Status shape: <c>status.nominal</c> (filled circle), <c>status.failed</c> (octagon), <c>status.suspect</c> (dashed triangle), <c>status.unknown</c> (dashed circle).</param>
/// <param name="Variant">Confirmed damage variant, for the inspector; null otherwise.</param>
/// <param name="Dimmed">Stale: draw in <c>text.mid</c> with the clock icon.</param>
/// <param name="Badge">e.g. <c>STALE 12m</c>; null when none.</param>
public sealed record NodeDisplay(
    DiagramLookState State,
    bool Stale,
    string? Variant,
    string Word,
    string ColorToken,
    string ShapeKey,
    bool DashedOutline,
    bool ThickBorder,
    bool Dimmed,
    string? Badge)
{
    private static readonly NodeDisplay UnknownDisplay = new(DiagramLookState.Unknown, false, null, "UNKNOWN", "unknown", "status.unknown", true, false, false, null);
    private static readonly NodeDisplay SuspectedDisplay = new(DiagramLookState.Suspected, false, null, "SUSPECT", "caution", "status.suspect", true, false, false, null);
    private static readonly NodeDisplay NominalDisplay = new(DiagramLookState.ConfirmedNominal, false, null, "NOMINAL", "ok", "status.nominal", false, false, false, null);
    private static readonly NodeDisplay FailedDisplay = new(DiagramLookState.ConfirmedDamaged, false, null, "FAILED", "critical", "status.failed", false, true, false, null);

    /// <param name="now">Current MET; a stale badge's age is derived from it, so it advances without a knowledge change.</param>
    public static NodeDisplay For(DiagramKnowledgeEntry knowledge, MissionTime now)
    {
        var look = knowledge.State switch
        {
            DiagramLookState.Suspected => SuspectedDisplay,
            DiagramLookState.ConfirmedNominal => NominalDisplay,
            DiagramLookState.ConfirmedDamaged => FailedDisplay with { Variant = knowledge.Variant },
            _ => UnknownDisplay,
        };
        if (!knowledge.Stale || knowledge.State is not (DiagramLookState.ConfirmedNominal or DiagramLookState.ConfirmedDamaged)) return look;
        return look with { Stale = true, Dimmed = true, Badge = StaleBadge(knowledge.ConfirmedAt, now) };
    }

    private static string StaleBadge(MissionTime? confirmedAt, MissionTime now)
    {
        if (confirmedAt is not { } at) return "STALE";
        var minutes = Math.Max(0, (now.Milliseconds - at.Milliseconds) / 60_000);
        return string.Create(CultureInfo.InvariantCulture, $"STALE {minutes}m");
    }
}
