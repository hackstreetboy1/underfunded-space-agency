namespace Usa.Game.Presentation.EntryHud;

/// <summary>The ground's prediction of the entry interface angle against the corridor (GDD 7.3).</summary>
/// <param name="EntryAngleDeg">Predicted flight-path angle at the entry interface, with its band (EST · ground predict).</param>
/// <param name="SteepestDeg">Corridor edge, e.g. −2.5°.</param>
/// <param name="ShallowestDeg">Corridor edge, e.g. −1.0°.</param>
public sealed record EntryCorridorInputs(HudReadout EntryAngleDeg, double SteepestDeg, double ShallowestDeg);

public enum CorridorVerdict
{
    Unknown,

    /// <summary>The whole predicted band lies inside the corridor.</summary>
    Ok,

    /// <summary>The band crosses a corridor edge.</summary>
    NearEdge,
    TooSteep,
    TooShallow,
}

/// <summary>The corridor badge beside the flight-path angle (Figma G4-03: <c>entry −1.3° (−2.5° to −1.0°)</c>, <c>CORRIDOR OK</c>).</summary>
/// <param name="LineText">e.g. <c>entry −1.3° (−2.5° to −1.0°)</c>; empty without words or a prediction.</param>
/// <param name="BadgeText">e.g. <c>CORRIDOR OK</c>, <c>NEAR EDGE</c>; empty when unknown.</param>
/// <param name="Readout">The predicted angle's state badge and <c>ground predict · MET …</c> meta line.</param>
public sealed record CorridorBadge(CorridorVerdict Verdict, string LineText, string BadgeText, HudTone Tone, HudReadoutRow Readout)
{
    public static CorridorBadge From(EntryCorridorInputs corridor, EntryHudWords words, double? nowMetS = null)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(words);
        var angle = corridor.EntryAngleDeg;
        var row = HudReadoutRow.From(angle, EntryHudFormat.AngleNumber, "°", nowMetS);
        if (!angle.HasValue) return new CorridorBadge(CorridorVerdict.Unknown, "", "", HudTone.Unknown, row);

        var low = double.IsFinite(angle.Low) ? Math.Min(angle.Low, angle.Value) : angle.Value;
        var high = double.IsFinite(angle.High) ? Math.Max(angle.High, angle.Value) : angle.Value;
        var (verdict, badge, tone) =
            low >= corridor.SteepestDeg && high <= corridor.ShallowestDeg ? (CorridorVerdict.Ok, words.CorridorOk, HudTone.Ok)
            : high < corridor.SteepestDeg ? (CorridorVerdict.TooSteep, words.CorridorTooSteep, HudTone.Critical)
            : low > corridor.ShallowestDeg ? (CorridorVerdict.TooShallow, words.CorridorTooShallow, HudTone.Caution)
            : (CorridorVerdict.NearEdge, words.CorridorNearEdge, HudTone.Caution);
        var line = EntryHudWords.Fill(words.CorridorLine,
            ("angle", EntryHudFormat.AngleDeg(angle.Value)),
            ("steep", EntryHudFormat.AngleDeg(corridor.SteepestDeg)),
            ("shallow", EntryHudFormat.AngleDeg(corridor.ShallowestDeg)));
        return new CorridorBadge(verdict, line, badge, tone, row);
    }
}
