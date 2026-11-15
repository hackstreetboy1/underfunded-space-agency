using System.Globalization;
using System.Text.RegularExpressions;
using Usa.Simulation.Commands;

namespace Usa.Game.Guidance;

/// <summary>
/// Equipment designators a tip names: thrusters ("T8", "thruster 8"), lettered rings and buses ("ring B", "Bus A") and
/// numbered parts ("Aux 3", "J-14", "fuel cell 2"). A rephrasing that names one the authored text does not points the
/// player at hardware the hint never chose, so it is rejected at every stage.
/// </summary>
public static partial class TipDesignators
{
    /// <summary>Canonical designators in the text: upper case, one space, no leading zeros ("T8", "RING B", "AUX 3").</summary>
    public static IReadOnlySet<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Pattern().Matches(AiGuardrails.Normalize(text)))
        {
            if (match.Groups["thruster"].Success) found.Add($"T{Number(match.Groups["thruster"].Value)}");
            else if (match.Groups["letter"].Success) found.Add($"{match.Groups["lettered"].Value.ToUpperInvariant()} {match.Groups["letter"].Value}");
            else found.Add($"{match.Groups["numbered"].Value.ToUpperInvariant()} {Number(match.Groups["number"].Value)}");
        }
        return found;
    }

    private static string Number(string digits) =>
        long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : digits;

    // Letters stay upper-case only ("ring B", never "ring a bell"); unit-bearing figures are the number rule's job.
    [GeneratedRegex(
        """
        (?<![\p{L}\p{N}])(?:
            [Tt](?<thruster>[0-9]{1,3})
          | (?i:thrusters?)[\p{Zs}\t]+(?:(?i:no\.?)[\p{Zs}\t]*)?(?<thruster>[0-9]{1,3})
          | (?<lettered>(?i:ring|bus|side|string|loop|leg))[\p{Zs}\t]+(?<letter>[A-D])
          | (?<numbered>(?i:aux|outlet|fuse|breaker|cell|battery|tank|panel|circuit|connector|valve|bus|ring|J))[\p{Zs}\t-]*(?<number>[0-9]{1,3})
        )(?![\p{L}\p{N}])
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
