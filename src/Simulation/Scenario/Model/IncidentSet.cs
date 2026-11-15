namespace Usa.Simulation.Scenario.Model;

/// <summary>Validated incidents and variants, each sorted by id.</summary>
public sealed record IncidentSet(IReadOnlyList<Variant> Variants, IReadOnlyList<Incident> Incidents);
