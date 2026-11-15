namespace Usa.Game.Presentation.EntryHud;

/// <summary>One crew health mini-bar (UI 4.2: stays visible during entry; UI 4.3 crew status).</summary>
/// <param name="Fraction01">Bar fill; 0 when unknown.</param>
/// <param name="HealthText"><c>92%</c> (at least <c>1%</c> while alive), or a dash when unknown.</param>
/// <param name="BandText">Band word of GDD 5.5, e.g. <c>Stable</c>.</param>
public sealed record CrewMini(
    string Name,
    double? HealthPercent,
    double Fraction01,
    CrewHealthBand Band,
    HudTone Tone,
    string HealthText,
    string BandText)
{
    public static CrewMini From(EntryCrewInputs crew)
    {
        ArgumentNullException.ThrowIfNull(crew);
        if (crew.HealthPercent is not { } health || !double.IsFinite(health))
        {
            return new CrewMini(crew.Name, null, 0, CrewHealthBand.Unknown, HudTone.Unknown, EntryHudFormat.Placeholder, "Unknown");
        }
        var clamped = Math.Clamp(health, 0, 100);
        var shown = ShownPercent(clamped);
        var band = BandOf(clamped);
        return new CrewMini(crew.Name, clamped, clamped / 100.0, band, ToneOf(band), EntryHudFormat.Percent(shown), band.ToString());
    }

    /// <summary>
    /// The whole percentage shown for a health value: rounded, but at least 1 while alive, so a living crew member never
    /// reads <c>0%</c>; only a true 0 shows 0.
    /// </summary>
    public static double ShownPercent(double healthPercent) =>
        healthPercent <= 0 ? 0 : Math.Max(1, Math.Round(healthPercent, MidpointRounding.AwayFromZero));

    /// <summary>
    /// GDD 5.5: Stable ≥ 80, Stressed 60-79, Impaired 35-59, Critical 1-34, Deceased 0. Bands follow
    /// <see cref="ShownPercent"/> so the word and the number agree; only a true 0 is Deceased.
    /// </summary>
    public static CrewHealthBand BandOf(double healthPercent)
    {
        if (healthPercent <= 0) return CrewHealthBand.Deceased;
        return ShownPercent(healthPercent) switch
        {
            >= 80 => CrewHealthBand.Stable,
            >= 60 => CrewHealthBand.Stressed,
            >= 35 => CrewHealthBand.Impaired,
            _ => CrewHealthBand.Critical,
        };
    }

    public static HudTone ToneOf(CrewHealthBand band) => band switch
    {
        CrewHealthBand.Stable => HudTone.Ok,
        CrewHealthBand.Stressed or CrewHealthBand.Impaired => HudTone.Caution,
        CrewHealthBand.Critical or CrewHealthBand.Deceased => HudTone.Critical,
        _ => HudTone.Unknown,
    };
}
