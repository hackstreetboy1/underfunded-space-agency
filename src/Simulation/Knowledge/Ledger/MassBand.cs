using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge.Ledger;

/// <summary>
/// A mass the ground knows only as a range, with where it came from (GDD 4.1: value, source, MET, state). Built from
/// knowledge and vehicle data only, never from the simulated true mass.
/// </summary>
/// <param name="Source">Display phrase for the basis, e.g. <c>ground ledger</c> or <c>gauge reading</c> (see <see cref="LedgerSources"/>).</param>
/// <param name="Met">When the knowledge behind the band was established; null when nothing was ever observed.</param>
public readonly record struct MassBand(double LowKg, double BestKg, double HighKg, string Source, MissionTime? Met, KnowledgeState State)
{
    public double WidthKg => HighKg - LowKg;

    /// <summary>Half the width: the <c>±</c> figure when the band is shown around its best value.</summary>
    public double HalfWidthKg => (HighKg - LowKg) / 2;

    public bool Contains(double massKg) => massKg >= LowKg && massKg <= HighKg;
}
