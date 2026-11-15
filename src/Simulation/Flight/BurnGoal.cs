namespace Usa.Simulation.Flight;

/// <summary>What the Easy-mode "Propose burn" assist should achieve.</summary>
public abstract record BurnGoal;

/// <summary>Retrograde burn bringing the periapsis down to an altitude.</summary>
public sealed record LowerPeriapsisTo(double PeriapsisKm) : BurnGoal;

/// <summary>Prograde burn raising the mean altitude (semi-major axis) by an amount.</summary>
public sealed record RaiseOrbitBy(double DeltaKm) : BurnGoal;

/// <summary>Retrograde burn timed (and sized) so the capsule lands in a recovery zone.</summary>
public sealed record TargetZone(string ZoneId) : BurnGoal;
