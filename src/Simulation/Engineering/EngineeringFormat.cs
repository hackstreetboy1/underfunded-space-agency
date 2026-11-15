using System.Globalization;

namespace Usa.Simulation.Engineering;

/// <summary>Invariant figures for check findings and part names (UI 6.2: units always, no trailing zeros).</summary>
internal static class EngineeringFormat
{
    public static string Amps(double value) => Number(value) + " A";

    public static string Metres(double value) => Number(value) + " m";

    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
