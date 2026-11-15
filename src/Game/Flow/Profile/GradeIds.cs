using System.Text.Json;
using Usa.Simulation.Scoring;

namespace Usa.Game.Flow.Profile;

/// <summary>Grade ids as stored in the profile (<c>config/scoring.json</c> spelling, e.g. <c>b_plus</c>) and their short labels.</summary>
public static class GradeIds
{
    public static string IdOf(Grade grade) => JsonNamingPolicy.SnakeCaseLower.ConvertName(grade.ToString());

    /// <summary>"s_plus" → "S+", "d_minus" → "D-", "b" → "B"; an unrecognised id is shown as is.</summary>
    public static string LabelOf(string gradeId)
    {
        ArgumentNullException.ThrowIfNull(gradeId);
        var parts = gradeId.Split('_');
        if (parts.Length is < 1 or > 2 || parts[0].Length != 1 || !char.IsAsciiLetter(parts[0][0])) return gradeId;
        var letter = char.ToUpperInvariant(parts[0][0]).ToString();
        if (parts.Length == 1) return letter;
        return parts[1] switch
        {
            "plus" => letter + "+",
            "minus" => letter + "-",
            _ => gradeId,
        };
    }
}
