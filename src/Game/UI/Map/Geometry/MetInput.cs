using System.Globalization;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>The planner's start-time field (G4-02): typed MET back to a <see cref="MissionTime"/>.</summary>
public static class MetInput
{
    /// <summary>
    /// Parses <c>07:43:40</c>, <c>7:43</c> (seconds 0) or with a leading <c>MET</c>; hours may pass 24.
    /// </summary>
    public static bool TryParse(string? text, out MissionTime time)
    {
        time = MissionTime.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("MET", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[3..].Trim();
        var parts = trimmed.Split(':');
        if (parts.Length is < 2 or > 3) return false;
        var values = new long[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;
            if (i > 0 && values[i] >= 60) return false;
        }
        time = new MissionTime(((values[0] * 3600) + (values[1] * 60) + values[2]) * 1000);
        return true;
    }
}
