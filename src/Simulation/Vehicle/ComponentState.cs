namespace Usa.Simulation.Vehicle;

/// <summary>
/// Mutable state of one component, stored densely in <see cref="VehicleState"/> and accessed by reference.
/// Only the fields of the component's family (and its load, if any) are meaningful.
/// </summary>
public struct ComponentState
{
    public ComponentCondition Condition { get; set; }

    /// <summary>Fault code, e.g. <c>short_on</c> or <c>open_circuit</c>; empty when none.</summary>
    public string Fault { get; set; }

    /// <summary>Switch, breaker, hatch or valve contacts closed.</summary>
    public bool Closed { get; set; }

    /// <summary>Thruster group armed (propellant valves open).</summary>
    public bool Armed { get; set; }

    /// <summary>Load enabled by its own control (independent of upstream breakers).</summary>
    public bool On { get; set; }

    /// <summary>Fuel cell or battery connected to its bus.</summary>
    public bool Online { get; set; }

    /// <summary>The load's feed is live, whatever the load's own condition (a shorted driver is energized but not powered).</summary>
    public bool Energized { get; set; }

    /// <summary>Load operating on power, or bus above brownout.</summary>
    public bool Powered { get; set; }

    /// <summary>Bus below its brownout voltage while loads are connected.</summary>
    public bool Brownout { get; set; }

    public double VoltageV { get; set; }

    /// <summary>Load: power drawn. Fuel cell: output. Battery: discharge minus charge.</summary>
    public double PowerW { get; set; }

    /// <summary>Thermal node: electrical heat released into it during the last electrical step.</summary>
    public double HeatW { get; set; }

    public double QuantityKg { get; set; }

    public bool Depleted { get; set; }

    public double EnergyKwh { get; set; }

    /// <summary>Fuel cell output factor (1 = rated).</summary>
    public double Efficiency { get; set; }

    /// <summary>Thruster valve fraction actually open during the last firing call.</summary>
    public double ValveOpen { get; set; }

    /// <summary>Fraction the valve is held open by a <c>short_on</c> driver fault whenever the driver is energized.</summary>
    public double StuckFraction { get; set; }

    public double TemperatureC { get; set; }

    /// <summary>Valve-open seconds accumulated since the last activity step.</summary>
    public double FiringSeconds { get; set; }

    /// <summary>Thruster or group mean valve fraction over the last activity step.</summary>
    public double Activity { get; set; }

    /// <summary>Sensor bias in the measured unit.</summary>
    public double Bias { get; set; }

    public bool Latched { get; set; }

    public double LoadFraction { get; set; }

    public double OverstressS { get; set; }

    public bool OverstressAlarm { get; set; }

    public bool Fired { get; set; }

    public double BurnRemainingS { get; set; }

    public ParachuteStatus Parachute { get; set; }

    /// <summary>Index of the component a repair jumper feeds this load from; -1 when none.</summary>
    public int Jumper { get; set; }
}
