using System.Globalization;
using Usa.Simulation.Scoring;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>
/// The band line under the debrief grade (Gate 6 decision 2, approved at its default): "Band C · 50–54", and for the top
/// band "Band S+ · 98–100, gated". Ranges come from the scoring config's grade bands.
/// </summary>
public static class GradeBandText
{
    public const int MaxPoints = 100;

    /// <summary>"50–54"; the last band runs to 100. Null when the grade has no band.</summary>
    public static string? Range(Grade grade, IReadOnlyList<GradeBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].Grade != grade) continue;
            var upper = i + 1 < bands.Count ? bands[i + 1].MinPoints - 1 : MaxPoints;
            return string.Create(CultureInfo.InvariantCulture, $"{bands[i].MinPoints}–{upper}");
        }
        return null;
    }

    /// <param name="label">The grade label shown huge (e.g. "C").</param>
    public static string Format(Grade grade, string label, IReadOnlyList<GradeBand> bands, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(text);
        var range = Range(grade, bands);
        if (range is null) return text.Format("debrief.band", Values(label, "–"));
        if (grade == Grade.SPlus) range = text.Format("debrief.bandGated", new Dictionary<string, string>(StringComparer.Ordinal) { ["range"] = range });
        return text.Format("debrief.band", Values(label, range));
    }

    private static Dictionary<string, string> Values(string grade, string range) =>
        new(StringComparer.Ordinal) { ["grade"] = grade, ["range"] = range };
}
