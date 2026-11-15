using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Ledger;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>Runs burn previews against the ground's current flight picture.</summary>
public interface IManeuverPreviewPort
{
    /// <summary>Current MET; plans never start before it.</summary>
    MissionTime Now { get; }

    /// <summary>False while the vehicle is mid-burn or landed.</summary>
    bool CanPreview { get; }

    /// <summary>
    /// Deprecated shim (X8 stage 2): the vehicle's mass as one number. Implement <see cref="MassBand"/> instead; this stays
    /// only until the map adapter supplies the ground ledger's band.
    /// </summary>
    double MassKg { get; }

    /// <summary>
    /// The ground ledger's mass of the vehicle (X8): preview physics only. The planner turns the <em>known</em> propellant
    /// into Δv against it and never displays it. The default wraps <see cref="MassKg"/> as an exact band.
    /// </summary>
    MassBand MassBand => new(MassKg, MassKg, MassKg, string.Empty, null, KnowledgeState.Known);

    /// <summary>Previews <paramref name="plan"/>; the plan's engine carries the propellant the ground knows, not the true load.</summary>
    /// <exception cref="PreviewUnavailableException">The vehicle cannot be previewed (check <see cref="CanPreview"/> first).</exception>
    ManeuverPreview Preview(ManeuverPlan plan);
}
