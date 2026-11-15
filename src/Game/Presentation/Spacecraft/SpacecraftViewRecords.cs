using Usa.Game.Presentation.Rendering;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>One vehicle's truth as the 3D view needs it. Vectors use the simulation's frames (see <see cref="SpacecraftAxes"/>).</summary>
/// <param name="Orientation">Body → inertial (Attitude <c>AttitudeVehicle.Orientation</c>).</param>
/// <param name="AngularVelocityRadPerSec">Body frame; used only to extrapolate between simulation steps.</param>
/// <param name="RelativePositionM">
/// Real metres, inertial axes, from the view's primary vehicle origin to this vehicle's origin (model origin: Tandem's
/// heat-shield plane, Lodestar's docking-cone face). Ignored for the primary vehicle and while docked, where the
/// models' docking ports decide the pose.
/// </param>
/// <param name="DockedWithId">The other vehicle of the docked stack, or null.</param>
/// <param name="EntryHeating01">Heat-rate proxy normalised to 0..1 (VFX-02b entry glow); 0 outside entry.</param>
public readonly record struct SpacecraftVehicleView(
    Quatd Orientation,
    Vec3d AngularVelocityRadPerSec,
    Vec3d RelativePositionM,
    string? DockedWithId,
    double EntryHeating01,
    bool SplashedDown);

/// <summary>Backdrop geometry, inertial axes (orbital plane = inertial X-Y, orbit normal +Z, as in Attitude).</summary>
/// <param name="NadirInertial">Unit vector from the vehicle to Earth's centre; zero when unknown.</param>
/// <param name="SunInertial">Unit vector from the vehicle to the Sun.</param>
public readonly record struct OrbitalBackdropView(Vec3d NadirInertial, Vec3d SunInertial, double AltitudeKm);

/// <summary>What the player knows about one component (Knowledge <c>ComponentKnowledge</c>, mapped by the adapter).</summary>
/// <param name="Variant">Confirmed damage variant word, e.g. <c>stuck_open</c>; used only with ConfirmedDamaged.</param>
public readonly record struct ComponentKnowledgeView(ComponentLookState State, string? Variant, bool Stale)
{
    public static readonly ComponentKnowledgeView Unknown = new(ComponentLookState.Unknown, null, false);
}

/// <summary>
/// The target vehicle's status panel (mission 1: Lodestar's, MODELING 9, GDD 12.5 I2) as the player knows it. A null field is
/// not known and its light stays dark; the panel never shows more than the player has learned (downlink or a crew glance).
/// </summary>
/// <param name="AcsActivity01">Mean attitude-thruster activity 0..1 (0.2 and up reads as "high activity").</param>
/// <param name="Latched">Docked and latched to the other vehicle.</param>
public readonly record struct LodestarPanelView(double? AcsActivity01, bool? Latched, bool? EngineArmed, bool? PowerOn)
{
    public static readonly LodestarPanelView Unknown = new(null, null, null, null);
}

/// <summary>How a firing component's plume looks (VFX-01, VFX-02a); the source maps it from the component's family.</summary>
public enum PlumeKind
{
    /// <summary>Not a thruster, engine or retro rocket of this mission.</summary>
    None,

    /// <summary>A small pulse thruster; its plume follows knowledge (see <see cref="ISpacecraftViewSource.GetFiringFraction"/>).</summary>
    AttitudeThruster,

    Engine,

    RetroRocket,
}

/// <summary>A crew member outside on EVA, as the player sees it (public crew location, no truth).</summary>
/// <param name="PackageRetrieved">The EVA's package has been retrieved (it rides in her hands).</param>
public readonly record struct EvaCrewView(string CrewId, bool PackageRetrieved);

/// <summary>A section a vehicle can drop (vehicle data <c>sections[].jettison</c>), in staging order.</summary>
/// <param name="SeparationEventId">The mission event that drops it, e.g. <c>equipment_sep</c>.</param>
public readonly record struct SeparableSection(string SectionId, string SeparationEventId);
