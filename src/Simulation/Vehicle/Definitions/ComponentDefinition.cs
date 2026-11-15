using Usa.Simulation.Kernel;

namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>
/// One component: common fields plus the typed sub-object for its family. <see cref="Load"/> may appear on any
/// family that draws bus power (drivers, fans, sensors, heaters...); every other sub-object must match the family.
/// </summary>
public sealed record ComponentDefinition
{
    public required ComponentId Id { get; init; }

    public required ComponentFamily Family { get; init; }

    public required string Name { get; init; }

    public required string Section { get; init; }

    /// <summary>True when inspection can reveal this component's condition in the 3D view.</summary>
    public bool Revealable { get; init; }

    public ControlAccess Control { get; init; } = ControlAccess.None;

    public DiagramPlacement? Diagram { get; init; }

    /// <summary>Sensor components that observe this component.</summary>
    public IReadOnlyList<ComponentId> Sensors { get; init; } = [];

    public LoadSpec? Load { get; init; }

    public ThrusterSpec? Thruster { get; init; }

    public ThrusterSpec? Engine { get; init; }

    public ThrusterGroupSpec? ThrusterGroup { get; init; }

    public TankSpec? Tank { get; init; }

    public SwitchSpec? Breaker { get; init; }

    public SwitchSpec? Switch { get; init; }

    public BusSpec? Bus { get; init; }

    public FuelCellSpec? FuelCell { get; init; }

    public BatterySpec? Battery { get; init; }

    public SensorSpec? Sensor { get; init; }

    public HarnessSpec? Harness { get; init; }

    public OutletSpec? Outlet { get; init; }

    public DockingSpec? Docking { get; init; }

    public ParachuteSpec? Parachute { get; init; }

    public RetroRocketSpec? RetroRocket { get; init; }

    public ThermalNodeSpec? ThermalNode { get; init; }

    public HeaterSpec? Heater { get; init; }

    public RadiatorSpec? Radiator { get; init; }

    public PumpSpec? Pump { get; init; }

    public FanSpec? Fan { get; init; }

    public ScrubberSpec? Scrubber { get; init; }

    public RegulatorSpec? Regulator { get; init; }

    public ValveSpec? Valve { get; init; }

    public HeatShieldSpec? HeatShield { get; init; }

    public PayloadSpec? Payload { get; init; }
}

/// <summary>Where the component is drawn on the systems diagram.</summary>
public sealed record DiagramPlacement(double X, double Y, string Compartment);
