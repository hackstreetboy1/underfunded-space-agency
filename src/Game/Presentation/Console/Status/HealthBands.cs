namespace Usa.Game.Presentation.Console.Status;

/// <summary>Maps health percent to the whole percent shown and its band, so the two never disagree.</summary>
public static class HealthBands
{
    /// <summary>Rounded percent as displayed; a living astronaut never reads 0 %.</summary>
    public static int DisplayPercent(double healthPercent)
    {
        if (!double.IsFinite(healthPercent)) return 0;
        var clamped = Math.Clamp(healthPercent, 0, 100);
        var rounded = (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
        return clamped > 0 && rounded < 1 ? 1 : rounded;
    }

    public static CrewHealthBand FromPercent(double healthPercent) => DisplayPercent(healthPercent) switch
    {
        >= 80 => CrewHealthBand.Stable,
        >= 60 => CrewHealthBand.Stressed,
        >= 35 => CrewHealthBand.Impaired,
        >= 1 => CrewHealthBand.Critical,
        _ => CrewHealthBand.Deceased,
    };
}
