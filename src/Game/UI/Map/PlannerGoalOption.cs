using Usa.Simulation.Flight;

namespace Usa.Game.UI.Map;

/// <summary>An entry of the Easy "Propose burn" goal picker (G4-02 Easy · Proposal).</summary>
/// <param name="Label">e.g. <c>Zone: Secondary West Pacific</c>, <c>Lower Pe into corridor</c>.</param>
/// <param name="Aside">Right-hand detail, e.g. the zone's recovery wait <c>3 h</c>; empty for none.</param>
public sealed record PlannerGoalOption(string Label, string Aside, BurnGoal Goal);
