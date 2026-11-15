namespace Usa.Simulation.LifeSupport.Atmosphere;

/// <summary>Inputs for one <see cref="CabinAtmosphere.Step"/>.</summary>
/// <param name="TempC">Cabin air temperature from the cabin thermal node.</param>
/// <param name="FanAirflowFraction">0-1: fan speed × power availability (0 when stopped or unpowered).</param>
/// <param name="O2SupplyAvailableKg">O2 the vehicle supply can deliver this step (tank contents, line limits).</param>
/// <param name="RegulatorEnabled">False when the regulator has failed closed or is isolated.</param>
/// <param name="MetabolicO2KgPerS">Total O2 use of the crew breathing cabin air.</param>
/// <param name="MetabolicCo2KgPerS">Total CO2 output of the crew breathing cabin air.</param>
public readonly record struct CabinStepInput(
    double TempC,
    double FanAirflowFraction,
    double O2SupplyAvailableKg,
    bool RegulatorEnabled,
    double MetabolicO2KgPerS,
    double MetabolicCo2KgPerS);

/// <summary>Mass flows of one cabin step; integration draws <see cref="O2DrawnKg"/> from the vehicle O2 supply.</summary>
/// <param name="O2RequestedKg">What the regulator wanted to add to reach the setpoint (flow-limited).</param>
/// <param name="O2ShortfallKg">Requested but not available from the supply.</param>
public readonly record struct CabinStepResult(
    double O2RequestedKg,
    double O2DrawnKg,
    double O2ShortfallKg,
    double O2ConsumedKg,
    double Co2ProducedKg,
    double Co2RemovedKg,
    double GasLeakedKg);

/// <summary>What a crew member breathing cabin air is exposed to (inputs to the crew health model).</summary>
public readonly record struct CabinExposure(double PpO2Mmhg, double PpCo2Mmhg, double PressureKpa, double TempC);
