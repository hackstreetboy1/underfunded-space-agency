using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Crew;

/// <summary>Health bar, percent and band word for a crew card or a resource-strip mini bar.</summary>
/// <param name="PercentText"><c>87%</c>, or <c>—</c> when unknown.</param>
/// <param name="Fraction">Bar fill 0-1; null when unknown.</param>
/// <param name="Band">Null when unknown.</param>
public sealed record CrewHealthView(string PercentText, double? Fraction, CrewHealthBand? Band, StatusVisual Visual)
{
    public static CrewHealthView Unknown(StatusLanguage status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(ReadoutFormatter.Placeholder, null, null, status.Unknown);
    }

    public static CrewHealthView From(CrewMemberSnapshot member, StatusLanguage status)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(status);
        if (member.DiedAt is not null) return Of(CrewHealthBand.Deceased, 0, status);
        if (member.HealthPercent is not { } health || !double.IsFinite(health)) return Unknown(status);
        var percent = HealthBands.DisplayPercent(health);
        return Of(HealthBands.FromPercent(health), percent, status);
    }

    private static CrewHealthView Of(CrewHealthBand band, int percent, StatusLanguage status) =>
        new(ReadoutFormatter.FormatPercent(percent), percent / 100.0, band, status.For(band));
}
