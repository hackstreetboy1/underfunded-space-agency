using Usa.Simulation.Kernel;

namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>Bus power drawn by a component. Fed from <paramref name="Bus"/> directly, or through whatever switch,
/// breaker, harness or jumper names this component as its <c>to</c>.</summary>
/// <param name="HeatW">Heat released into <paramref name="ThermalNode"/> while powered; defaults to <paramref name="PowerW"/>.</param>
public sealed record LoadSpec(
    double PowerW,
    LoadPriority Priority,
    ComponentId? Bus = null,
    double? HeatW = null,
    ComponentId? ThermalNode = null,
    bool InitiallyOn = true);

/// <summary>A thruster or engine. <paramref name="DirectionUnit"/> is the direction of the force on the vehicle
/// (opposite to the exhaust); torque = position × force.</summary>
/// <param name="Driver">Component whose power opens the valve; null when the thruster has no driver.</param>
/// <param name="Group">Thruster group that must be armed; null when ungrouped.</param>
/// <param name="ColdC">Steady temperature when idle; firing continuously drives it towards <paramref name="HotC"/>.</param>
public sealed record ThrusterSpec(
    double ThrustN,
    double IspS,
    IReadOnlyList<double> PositionM,
    IReadOnlyList<double> DirectionUnit,
    ComponentId Tank,
    ComponentId? Driver = null,
    ComponentId? Group = null,
    double ColdC = 10,
    double HotC = 10,
    double ThermalTimeConstantS = 60);

/// <param name="LowActivityFraction">Mean member valve fraction above which activity reads 'low'.</param>
/// <param name="HighActivityFraction">Mean member valve fraction above which activity reads 'high'.</param>
public sealed record ThrusterGroupSpec(
    bool InitiallyArmed = false,
    double LowActivityFraction = 0.01,
    double HighActivityFraction = 0.1);

/// <param name="Resource">What the tank holds: propellant, o2, h2, n2, water.</param>
/// <param name="PositionM">Centre of the contents in the vehicle frame (point mass for inertia).</param>
public sealed record TankSpec(string Resource, double CapacityKg, double InitialKg, IReadOnlyList<double> PositionM);

/// <summary>A breaker or switch. Closed, it connects <paramref name="From"/> (a bus, outlet or upstream switch) to
/// <paramref name="To"/>; switches without <paramref name="To"/> are junctions that downstream switches name as their source.</summary>
public sealed record SwitchSpec(ComponentId From, ComponentId? To = null, bool InitiallyClosed = true);

/// <param name="DroopV">Voltage drop from no load to full rated load.</param>
public sealed record BusSpec(double NominalV, double BrownoutV, double DroopV);

/// <param name="ReactantKgPerKwh">Total O2 + H2 consumed per kWh, split by the water mass ratio (8:1). The product water
/// has the same mass.</param>
/// <param name="WasteHeatFraction">Heat released per watt of electrical output.</param>
/// <param name="WaterLPerKwh">Legacy and ignored: water output follows from <paramref name="ReactantKgPerKwh"/> (mass is
/// conserved). Kept optional only so existing data still loads; remove it from data, then from here.</param>
public sealed record FuelCellSpec(
    ComponentId Bus,
    double MaxPowerW,
    ComponentId O2Tank,
    ComponentId H2Tank,
    double ReactantKgPerKwh,
    ComponentId WaterTank,
    double WasteHeatFraction = 0,
    ComponentId? ThermalNode = null,
    bool InitiallyOnline = true,
    double? WaterLPerKwh = null);

public sealed record BatterySpec(
    ComponentId Bus,
    double CapacityKwh,
    double InitialKwh,
    double MaxDischargeW,
    double MaxChargeW = 0,
    bool InitiallyOnline = true,
    ComponentId? ThermalNode = null);

/// <summary>A sensor. Each channel measures one state path; noise and bias are in that path's unit.</summary>
/// <param name="DegradedNoiseFactor">Noise multiplier while the sensor is degraded.</param>
public sealed record SensorSpec(
    IReadOnlyList<SensorChannelSpec> Channels,
    double NoiseStd = 0,
    double Quantization = 0,
    double InitialBias = 0,
    double DegradedNoiseFactor = 3);

/// <param name="Field">Name under which the channel is exposed on the sensor component (truth when online).</param>
/// <param name="Measures">State path measured, without '@'.</param>
public sealed record SensorChannelSpec(string Field, string Measures);

public sealed record HarnessSpec(ComponentId From, ComponentId To, double RatingA = 0);

/// <param name="Connector">Connector type for the engineering workspace (e.g. d_type, round).</param>
public sealed record OutletSpec(ComponentId Bus, string Connector = "", double RatingA = 0);

/// <summary>One half of a docking interface. The target-side thresholds drive load and overstress damage.</summary>
/// <param name="Partner">The mating component on the other vehicle.</param>
/// <param name="FullLoadRollRateDps">Roll rate at which the structural load reads 100 %.</param>
/// <param name="AlarmRollRateDps">Roll rate above which the load alarm is active and overstress time accumulates.</param>
/// <param name="DamageAfterS">Continuous overstress time after which the interface is damaged.</param>
public sealed record DockingSpec(
    ComponentId Partner,
    double FullLoadRollRateDps,
    double AlarmRollRateDps,
    double DamageAfterS,
    bool LatchedInitially = false);

public sealed record ParachuteSpec(double DragAreaM2, double DragCoefficient, double NominalDeployAltitudeKm);

/// <param name="IgnitionOffsetS">Seconds after the retrofire sequence starts that this rocket ignites (the rockets fire in an overlapping ripple).</param>
public sealed record RetroRocketSpec(
    double ThrustN,
    double BurnS,
    double PropellantKg,
    IReadOnlyList<double> PositionM,
    IReadOnlyList<double> DirectionUnit,
    double IgnitionOffsetS = 0);

/// <param name="SolarAbsorbedPeakW">Solar heat the node absorbs in full sun at sun pitch 0 (scaled by <paramref name="Solar"/>'s pitch curve).</param>
/// <param name="Solar">How the solar heating depends on attitude; needs the mission's <c>session.json</c> <c>sun</c>.</param>
public sealed record ThermalNodeSpec(
    double HeatCapacityJPerK,
    double InitialC,
    double MinC,
    double MaxC,
    double SolarAbsorbedPeakW = 0,
    ThermalSolarSpec? Solar = null);

/// <summary>
/// A thermal node's attitude-dependent solar heating: <paramref name="PitchCurve"/> pairs <c>[|sun pitch| deg, factor]</c> scale
/// the node's peak; <paramref name="Surfaces"/> absorb sunlight by incidence instead (a node with surfaces has no peak).
/// </summary>
public sealed record ThermalSolarSpec(
    IReadOnlyList<IReadOnlyList<double>>? PitchCurve = null,
    IReadOnlyList<ThermalSolarSurfaceSpec>? Surfaces = null);

/// <param name="Normal">Outward normal in the vehicle body frame (x nose, y pitch axis, z yaw axis).</param>
public sealed record ThermalSolarSurfaceSpec(IReadOnlyList<double> Normal, double AreaM2, double Absorptivity);

/// <param name="Node">Thermal node the heater warms; its power comes from the component's load.</param>
public sealed record HeaterSpec(ComponentId Node, double SetpointC, double HysteresisC);

public sealed record RadiatorSpec(ComponentId Node, double AreaM2, double Emissivity, double Absorptivity);

public sealed record PumpSpec(double FlowKgPerS);

public sealed record FanSpec(double AirflowM3PerS);

public sealed record ScrubberSpec(double CapacityKgCo2, double RemovalEfficiency);

/// <param name="Tank">Primary supply tank.</param>
/// <param name="SecondaryTank">Backup supply tank, if any.</param>
public sealed record RegulatorSpec(ComponentId Tank, double SetpointKpa, double MaxFlowKgPerS, ComponentId? SecondaryTank = null);

public sealed record ValveSpec(bool InitiallyOpen = true);

public sealed record HeatShieldSpec(double AblatorKg);

public sealed record PayloadSpec(double MassKg);
