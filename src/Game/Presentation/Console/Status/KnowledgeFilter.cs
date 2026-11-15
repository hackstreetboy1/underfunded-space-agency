using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Status;

/// <summary>
/// Enforces GDD 4 at the view-model boundary: a readout keeps only the fields its knowledge state may show,
/// so a value the ground has not earned can never reach a label, a trend arrow or a sparkline.
/// </summary>
public static class KnowledgeFilter
{
    /// <param name="now">Current MET; a stale readout's age is measured from its observation to here.</param>
    public static ReadoutSnapshot Sanitize(ReadoutSnapshot snapshot, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Knowledge switch
        {
            ReadoutKnowledge.Known => snapshot with { StaleAge = null, RangeMin = null, RangeMax = null, ConflictValue = null, GroundBand = null },
            ReadoutKnowledge.Stale => snapshot with
            {
                StaleAge = StaleAge(snapshot, now),
                RangeMin = null,
                RangeMax = null,
                ConflictValue = null,
                GroundBand = null,
            },
            ReadoutKnowledge.Estimated => snapshot with { StaleAge = null, ConflictValue = null, GroundBand = null },
            ReadoutKnowledge.Conflict => snapshot with { StaleAge = null, RangeMin = null, RangeMax = null, GroundBand = null },
            ReadoutKnowledge.GroundBand => new ReadoutSnapshot(ReadoutKnowledge.GroundBand, snapshot.Unit)
            {
                GroundBand = snapshot.GroundBand,
                Source = snapshot.Source,
                ObservedAt = snapshot.ObservedAt,
            },
            _ => ReadoutSnapshot.UnknownIn(snapshot.Unit),
        };
    }

    /// <summary>The value a sparkline may plot: fresh, verified readings only.</summary>
    public static double? PlottableValue(ReadoutSnapshot sanitized)
    {
        ArgumentNullException.ThrowIfNull(sanitized);
        return sanitized.Knowledge == ReadoutKnowledge.Known && sanitized.Value is { } value && double.IsFinite(value) ? value : null;
    }

    private static TimeSpan? StaleAge(ReadoutSnapshot snapshot, MissionTime now)
    {
        if (snapshot.ObservedAt is not { } observedAt) return snapshot.StaleAge;
        return TimeSpan.FromMilliseconds(Math.Max(0, now.Milliseconds - observedAt.Milliseconds));
    }
}
