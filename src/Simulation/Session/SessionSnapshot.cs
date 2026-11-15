using Usa.Simulation.Forecast;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Time;

namespace Usa.Simulation.Session;

/// <summary>
/// A read-only picture of the session for presentation, built on demand (it allocates; call it per frame, not per tick).
/// Panels that show readings use <see cref="Facts"/> (what the crew and ground know); <see cref="Resources"/> and the vehicle,
/// attitude and flight views are truth, for the 3D view, debug overlays and time compression.
/// </summary>
public sealed record SessionSnapshot(
    MissionTime Now,
    bool IsFrozen,
    FreezeReason FreezeReasons,
    string Phase,
    bool Ended,
    string Outcome,
    IReadOnlyList<VehicleView> Vehicles,
    IReadOnlyList<AttitudeView> Attitude,
    FlightView Flight,
    IReadOnlyList<CrewView> Crew,
    TaskSlotView? Task,
    ResourcesView Resources,
    IReadOnlyList<FactView> Facts,
    IReadOnlyList<ComponentKnowledge> Components,
    IReadOnlyList<ActiveAlarm> Alarms,
    IReadOnlyList<ObjectiveView> Objectives,
    ForecastResult? Forecast,
    CompressionInputs Compression,
    TipAvailable? Tip,
    string? TutorialBeat);

public sealed record SectionView(string Id, bool Attached);

public sealed record VehicleView(string Id, double MassKg, double PowerGenerationW, double PowerLoadW, IReadOnlyList<SectionView> Sections);

/// <param name="Orientation">Vehicle → inertial, for the 3D view.</param>
public sealed record AttitudeView(
    string VehicleId,
    double RollDps,
    double PitchDps,
    double YawDps,
    double TotalDps,
    string Mode,
    string ControlSet,
    double PointingDeg,
    double FiringDuty,
    string? DockedWith,
    Quatd Orientation);

public sealed record FlightView(
    string Phase,
    bool Docked,
    double AltitudeKm,
    double PeriapsisKm,
    double ApoapsisKm,
    double SpeedMps,
    double GLoad,
    double RangeM,
    bool DrogueDeployed,
    bool MainDeployed,
    bool BurnActive,
    string Zone,
    double RecoveryWaitHours);

public sealed record CrewView(CrewId Id, string Name, double Health, string Band, bool Alive, string Location, string Activity);

/// <param name="Progress">Fraction of the work done, 0-1.</param>
public sealed record TaskSlotView(TaskId Task, CrewId Assignee, CommandId Origin, MissionTime StartedAt, MissionTime EndsAt, double Progress);

/// <summary>True consumables and cabin state of the crewed vehicle.</summary>
public sealed record ResourcesView(
    double CabinPpO2Mmhg,
    double CabinPpCo2Mmhg,
    double CabinPressureKpa,
    double CabinTempC,
    double LiOhPct,
    double O2SupplyKg,
    double FoodKcal,
    double WaterL);

/// <summary>What is known about a fact: state text (<c>unknown</c>, <c>known</c>, <c>stale</c>, <c>conflicting</c>, <c>estimated</c>) and the latest report.</summary>
public sealed record FactView(FactId Id, string Label, string Unit, string State, double? Value, string? Text, MissionTime? ObservedAt, string? GroundBand);

/// <param name="State">'achievable' | 'completed' | 'unacceptable_risk' | 'impossible'.</param>
/// <param name="Cause">'' | 'scenario' | 'player_avoidable'.</param>
public sealed record ObjectiveView(ObjectiveId Id, string Title, string Kind, string State, string Cause);
