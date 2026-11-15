namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Everything the external 3D view reads, per frame, from the running mission. The session adapter implements it
/// over Attitude, Flight, Vehicle and Knowledge; the view never sees those areas. Reads must be cheap and must not
/// allocate: the view calls them every frame (the 3D attitude has to move smoothly), unlike the 10 Hz UI readouts.
/// <para>
/// Ids are vehicle-JSON ids (mission 1: vehicles <c>tandem</c>, <c>lodestar</c>; components <c>tandem/oams/thruster_08</c>;
/// sections <c>adapter</c>, <c>retro</c>); the view knows no mission's ids and takes the vehicles' roles from
/// <see cref="PrimaryVehicleId"/> and <see cref="TargetVehicleId"/>. Truth (poses, staging) and knowledge (component
/// looks) are separate on purpose: a component's colour and damage variant come only from <see cref="GetComponentKnowledge"/>.
/// </para>
/// <para>
/// <b>Presentation is knowledge-only (GDD 4.2, 4.4).</b> Every value here that is not a pose, staging or heating must
/// express only what the player knows. In particular <see cref="GetFiringFraction"/> is gated by the plume rule.
/// </para>
/// </summary>
public interface ISpacecraftViewSource
{
    /// <summary>The crewed vehicle; the scene is centred on it and the others are placed relative to it.</summary>
    string PrimaryVehicleId { get; }

    /// <summary>The vehicle the crew docks with (it carries the status panel), or null when the mission has none.</summary>
    string? TargetVehicleId { get; }

    /// <summary>The target's status-panel component (<see cref="LodestarStatusLights"/>), or null when it has none.</summary>
    string? StatusPanelComponentId { get; }

    /// <summary>Sun and Earth directions for the backdrop; may change slowly every frame.</summary>
    OrbitalBackdropView Backdrop { get; }

    /// <summary>
    /// Mission elapsed time, seconds; turns the Earth under the vehicles, drifts its clouds and paces MET-based looks (the
    /// stuck plume needs firing that lasts in MET, so a held frame during a freeze never turns into it).
    /// </summary>
    double MetSec { get; }

    /// <summary>Incremented whenever any component's knowledge (look state, variant, stale) changes.</summary>
    int KnowledgeRevision { get; }

    /// <summary>False when the vehicle is not part of this mission (its rig is hidden).</summary>
    bool TryGetVehicle(string vehicleId, out SpacecraftVehicleView vehicle);

    /// <summary>
    /// Firing fraction 0..1 of a thruster, retro rocket or engine over the last simulation step, as the player may see
    /// it (plume rule, GDD 4.4): engines and retro rockets as they fire (on the player's order); an attitude thruster 0
    /// until the player has confirmed its condition; once confirmed damaged, its true firing; once confirmed nominal, only
    /// the commanded firing, never an opening a later, unconfirmed fault added. 0 for ids the source does not know.
    /// </summary>
    double GetFiringFraction(string vehicleId, string thrusterComponentId);

    /// <summary>The plume look of a component (<see cref="PlumeKind.None"/> for ids that never fire).</summary>
    PlumeKind GetPlumeKind(string componentId);

    /// <summary>Sections <paramref name="vehicleId"/> can drop, in staging order; empty when none.</summary>
    IReadOnlyList<SeparableSection> GetSeparableSections(string vehicleId);

    bool IsSectionJettisoned(string vehicleId, string sectionId);

    ChuteDeployState GetChuteState(string vehicleId, string chuteComponentId);

    /// <summary>
    /// The vehicle-data <c>revealable</c> flag: false for parts that always show as built (windows, handrail,
    /// packed chutes). Those are drawn in colour and never take knowledge.
    /// </summary>
    bool IsRevealable(string componentId);

    ComponentKnowledgeView GetComponentKnowledge(string componentId);

    /// <summary>
    /// The target vehicle's status panel as the player knows it: the status downlink or the crew's latest glance at the
    /// panel (Knowledge); null fields stay dark. Never derived from truth, so the default knows nothing.
    /// </summary>
    LodestarPanelView GetLodestarPanel() => LodestarPanelView.Unknown;

    /// <summary>
    /// After splashdown, the share 0..1 of the recovery wait the player's recovery card shows as elapsed (UI 4.3, GDD 7.3):
    /// the ship closes in from the horizon and the helicopter arrives late in the wait. Null (the default) shows the
    /// recovery forces on scene.
    /// </summary>
    double? RecoveryProgress01 => null;

    /// <summary>The crew member currently outside on EVA (public crew location), or null when nobody is.</summary>
    EvaCrewView? EvaCrew => null;

    /// <summary>
    /// While the crew flies the docking (the task slot shows a task that docks the vehicles), its progress 0..1 as the task
    /// slot shows it; null otherwise (the default). The view flies the approach from it: progress, not frame time, so it
    /// looks the same at any time-compression rate and holds while frozen. Public crew-task state only, never truth.
    /// </summary>
    double? DockingApproach01 => null;
}
