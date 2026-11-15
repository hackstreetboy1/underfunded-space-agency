using System.Globalization;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Simulation.Knowledge;

/// <summary>Formats observed values per UI 6.2: SI units, mass 0 dp at 100 or more else 1 dp, whole percentages, signed rates.</summary>
public static class FactValueFormatter
{
    /// <summary>U+2212, as in the UI number rules (<c>−4.2 kg/h</c>).</summary>
    private const char Minus = '−';

    public static string Format(FactDefinition fact, Observation observation)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Value is not { } value) return Humanize(observation.Qualitative ?? "");

        var text = fact.Format switch
        {
            FactFormat.Mass => Number(value, Math.Abs(Math.Round(value, 1, MidpointRounding.AwayFromZero)) >= 100 ? 0 : 1, signed: false),
            FactFormat.Percent => Number(value, 0, signed: false),
            FactFormat.Rate => Number(value, 1, signed: true),
            _ => Number(value, fact.Decimals, signed: false),
        };
        if (fact.Unit.Length == 0) return text;
        return fact.Unit == "%" ? text + "%" : $"{text} {fact.Unit}";
    }

    /// <summary>MET as HH:MM (hours keep counting past 24).</summary>
    public static string HoursMinutes(MissionTime time)
    {
        var minutes = time.Milliseconds / 60_000;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
    }

    /// <summary>Turns a data word into speech: <c>failed:stuck_open</c> becomes <c>failed: stuck open</c>.</summary>
    public static string Humanize(string word) => word.Replace('_', ' ').Replace(":", ": ", StringComparison.Ordinal);

    private static string Number(double value, int decimals, bool signed)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        var magnitude = Math.Abs(rounded).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (rounded < 0) return Minus + magnitude;
        return signed && rounded > 0 ? "+" + magnitude : magnitude;
    }
}
