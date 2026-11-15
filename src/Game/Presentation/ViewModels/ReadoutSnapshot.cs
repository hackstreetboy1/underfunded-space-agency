using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// Everything a readout shows, replaced as a whole so one change is one notification
/// ("value, unit, source, MET timestamp and state", GDD 4.1).
/// </summary>
/// <param name="Unit">SI unit as displayed (<c>kg</c>, <c>%</c>, <c>kWh</c>, <c>°C</c>); empty for unitless values.</param>
public sealed record ReadoutSnapshot(ReadoutKnowledge Knowledge, string Unit)
{
    /// <summary>The reading (Known, Stale, Conflict) or central estimate (Estimated, when no range is given).</summary>
    public double? Value { get; init; }

    /// <summary>Who or what reported it, e.g. <c>Okafor</c> or <c>Instrument</c>.</summary>
    public string? Source { get; init; }

    /// <summary>MET of the observation. With the current MET (<see cref="ReadoutViewModel.SetMissionTime"/>) it also drives the <c>STALE 12m</c> age.</summary>
    public MissionTime? ObservedAt { get; init; }

    /// <summary>Signed rate of change in <see cref="Unit"/> per hour of MET.</summary>
    public double? TrendPerHour { get; init; }

    /// <summary>
    /// MET elapsed since the observation, for the <c>STALE 12m</c> badge when the current MET is not known. A fixed
    /// age freezes until the next push, so prefer <see cref="ObservedAt"/> plus <see cref="ReadoutViewModel.SetMissionTime"/>.
    /// </summary>
    public TimeSpan? StaleAge { get; init; }

    /// <summary>Estimated range, shown as <c>≈7.5–9 h</c>.</summary>
    public double? RangeMin { get; init; }

    public double? RangeMax { get; init; }

    /// <summary>The disagreeing second observation for <see cref="ReadoutKnowledge.Conflict"/>.</summary>
    public double? ConflictValue { get; init; }

    /// <summary>Who reported <see cref="ConflictValue"/> (GDD 4.1: a conflict shows both values with attribution).</summary>
    public string? ConflictSource { get; init; }

    /// <summary>MET of the <see cref="ConflictValue"/> observation.</summary>
    public MissionTime? ConflictObservedAt { get; init; }

    /// <summary>Band word for <see cref="ReadoutKnowledge.GroundBand"/>, e.g. <c>LOW</c>.</summary>
    public string? GroundBand { get; init; }

    public static ReadoutSnapshot UnknownIn(string unit) => new(ReadoutKnowledge.Unknown, unit);
}
