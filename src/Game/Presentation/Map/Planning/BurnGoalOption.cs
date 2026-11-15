using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

public enum BurnGoalKind
{
    LowerPeriapsis,
    TargetZone,
    RaiseOrbit,
}

/// <summary>An entry of the Easy "Propose burn" goal picker (Figma G4-02, UI 4.1).</summary>
/// <param name="Label"><c>Lower Pe into corridor</c>, <c>Zone: Secondary West Pacific</c>, <c>Raise orbit by…</c>.</param>
/// <param name="Aside">Right-hand detail (empty for lower Pe, as approved): the zone's recovery wait (<c>3 h</c>), the raise amount or its unit (<c>km</c>).</param>
/// <param name="Goal">What Propose asks for; null while the entry still needs an amount (<c>Raise orbit by…</c>).</param>
public sealed record BurnGoalOption(BurnGoalKind Kind, string Label, string Aside, BurnGoal? Goal)
{
    public bool NeedsAmount => Goal is null;
}

/// <summary>The goal picker's entries in the approved order: lower Pe, each zone, raise orbit (Figma G4-02 "Easy · goal picker, open").</summary>
public static class BurnGoalOptions
{
    /// <param name="zones">The map's zone arcs (label and recovery wait), in map order.</param>
    /// <param name="corridorPeriapsisKm">The frame's <see cref="OrbitMapFrame.CorridorPeriapsisKm"/>; null leaves the lower-Pe entry out.</param>
    /// <param name="raiseByKm">The amount entered for the raise; null lists the entry as needing one.</param>
    public static IReadOnlyList<BurnGoalOption> Build(
        MapText text, IReadOnlyList<RecoveryZoneArc> zones, double? corridorPeriapsisKm, double? raiseByKm = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zones);
        var options = new List<BurnGoalOption>(zones.Count + 2);
        if (corridorPeriapsisKm is { } periapsisKm && double.IsFinite(periapsisKm))
            options.Add(new BurnGoalOption(BurnGoalKind.LowerPeriapsis, text.Get("planner.goalLowerPe"), "",
                new LowerPeriapsisTo(periapsisKm)));
        foreach (var zone in zones)
        {
            options.Add(new BurnGoalOption(BurnGoalKind.TargetZone, text.Format("planner.goalZone", ("zone", TitleCase(zone.Label))),
                FlightFormat.Hours(zone.WaitHours), new TargetZone(zone.ZoneId)));
        }
        options.Add(raiseByKm is { } km && km > 0 && double.IsFinite(km)
            ? new BurnGoalOption(BurnGoalKind.RaiseOrbit, text.Get("planner.goalRaiseOrbit"), FlightFormat.AltitudeKm(km), new RaiseOrbitBy(km))
            : new BurnGoalOption(BurnGoalKind.RaiseOrbit, text.Get("planner.goalRaiseOrbit"), text.Get("planner.goalRaiseOrbitUnit"), null));
        return options;
    }

    /// <summary>Rim labels are capitals (<c>SECONDARY WEST PACIFIC</c>); the picker reads <c>Secondary West Pacific</c>.</summary>
    private static string TitleCase(string label)
    {
        var words = label.ToLowerInvariant().Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}
