using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Atmosphere;

/// <summary>
/// Well-mixed ideal-gas cabin tracking O2, CO2 and inert masses. Each step: crew metabolism, LiOH removal
/// (efficiency × airflow × capacity factor, integrated exactly), leakage, then the O2 regulator tops ppO2 up to
/// its setpoint from whatever the supply can deliver. Regulator and scrubber values in <see cref="CabinSpec"/> override
/// the global configuration for this vehicle.
/// </summary>
public sealed class CabinAtmosphere : IHashContributor
{
    private readonly RegulatorConfig _regulator;
    private readonly ScrubberConfig _scrubber;
    private double _tempK;

    public CabinAtmosphere(CabinSpec spec, RegulatorConfig regulator, ScrubberConfig scrubber, double initialTempC)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(regulator);
        ArgumentNullException.ThrowIfNull(scrubber);
        var errors = new DefinitionErrors();
        spec.Validate(errors, $"cabin {spec.Id}");
        errors.ThrowIfAny();

        Spec = spec;
        _regulator = new RegulatorConfig(
            spec.PpO2SetpointMmhg ?? regulator.PpO2SetpointMmhg, spec.MaxO2FlowKgPerHour ?? regulator.MaxFlowKgPerHour);
        _scrubber = scrubber with { Efficiency = spec.ScrubberEfficiency ?? scrubber.Efficiency };
        _tempK = initialTempC + PhysicalConstants.ZeroCelsiusInKelvin;
        O2Kg = MassFromMmhg(spec.InitialPpO2Mmhg, GasConstants.MolarMassO2KgPerMol);
        Co2Kg = MassFromMmhg(spec.InitialPpCo2Mmhg, GasConstants.MolarMassCo2KgPerMol);
        InertKg = MassFromMmhg(spec.InitialInertMmhg, GasConstants.MolarMassInertKgPerMol);
        LeakPctPerHour = spec.LeakPctPerHour;
        LiOhRemainingKgCo2 = spec.LiOhCapacityKgCo2;
    }

    public CabinSpec Spec { get; }

    public string Id => Spec.Id;

    public ComponentId ThermalNode => Spec.ThermalNode;

    public double O2Kg { get; private set; }

    public double Co2Kg { get; private set; }

    public double InertKg { get; private set; }

    public double TempC => _tempK - PhysicalConstants.ZeroCelsiusInKelvin;

    public double PpO2Mmhg => MmhgOf(O2Kg, GasConstants.MolarMassO2KgPerMol);

    /// <summary>ppO2 in kPa (the unit of the health model's hypoxia curve).</summary>
    public double PpO2Kpa => KpaFromMmhg(PpO2Mmhg);

    public double PpCo2Mmhg => MmhgOf(Co2Kg, GasConstants.MolarMassCo2KgPerMol);

    public double InertMmhg => MmhgOf(InertKg, GasConstants.MolarMassInertKgPerMol);

    public double PressureKpa => KpaFromMmhg(PpO2Mmhg + PpCo2Mmhg + InertMmhg);

    public double LeakPctPerHour { get; private set; }

    public double LiOhRemainingKgCo2 { get; private set; }

    public double LiOhPct => LiOhRemainingKgCo2 / Spec.LiOhCapacityKgCo2 * 100.0;

    /// <summary>1 while the canister is above breakthrough, falling linearly to 0 as it saturates.</summary>
    public double ScrubberCapacityFactor =>
        Math.Clamp(LiOhRemainingKgCo2 / Spec.LiOhCapacityKgCo2 / _scrubber.BreakthroughFraction, 0.0, 1.0);

    public int CanistersReplaced { get; private set; }

    /// <summary>True when the last step's regulator demand could not be met by the supply.</summary>
    public bool O2SupplyShort => LastStep.O2ShortfallKg > 0;

    public CabinStepResult LastStep { get; private set; }

    public CabinExposure Exposure => new(PpO2Mmhg, PpCo2Mmhg, PressureKpa, TempC);

    public CabinStepResult Step(double deltaSeconds, in CabinStepInput input)
    {
        if (deltaSeconds <= 0) return LastStep = default;
        Finite.Require(input.TempC, nameof(input.TempC));
        Finite.Require(input.FanAirflowFraction, nameof(input.FanAirflowFraction));
        Finite.Require(input.O2SupplyAvailableKg, nameof(input.O2SupplyAvailableKg));
        Finite.Require(input.MetabolicO2KgPerS, nameof(input.MetabolicO2KgPerS));
        Finite.Require(input.MetabolicCo2KgPerS, nameof(input.MetabolicCo2KgPerS));
        _tempK = Math.Max(1.0, input.TempC + PhysicalConstants.ZeroCelsiusInKelvin);

        var o2Consumed = Math.Min(O2Kg, Math.Max(0, input.MetabolicO2KgPerS) * deltaSeconds);
        var co2Produced = Math.Max(0, input.MetabolicCo2KgPerS) * deltaSeconds;
        O2Kg -= o2Consumed;
        Co2Kg += co2Produced;

        var airflow = Math.Max(Math.Clamp(input.FanAirflowFraction, 0.0, 1.0), _scrubber.PassiveAirflowFraction);
        var removalPerS = _scrubber.Efficiency * airflow * Spec.FanAirflowM3PerS / Spec.VolumeM3 * ScrubberCapacityFactor;
        var co2Removed = Math.Min(Co2Kg * -double.ExpM1(-removalPerS * deltaSeconds), LiOhRemainingKgCo2);
        Co2Kg -= co2Removed;
        LiOhRemainingKgCo2 -= co2Removed;

        var leakFraction = -double.ExpM1(-LeakPctPerHour / 100.0 / GasConstants.SecondsPerHour * deltaSeconds);
        var leaked = (O2Kg + Co2Kg + InertKg) * leakFraction;
        O2Kg -= O2Kg * leakFraction;
        Co2Kg -= Co2Kg * leakFraction;
        InertKg -= InertKg * leakFraction;

        var requested = 0.0;
        if (input.RegulatorEnabled)
        {
            var targetKg = MassFromMmhg(_regulator.PpO2SetpointMmhg, GasConstants.MolarMassO2KgPerMol);
            var maxFlowKg = _regulator.MaxFlowKgPerHour / GasConstants.SecondsPerHour * deltaSeconds;
            requested = Math.Clamp(targetKg - O2Kg, 0.0, maxFlowKg);
        }
        var drawn = Math.Min(requested, Math.Max(0, input.O2SupplyAvailableKg));
        O2Kg += drawn;

        return LastStep = new CabinStepResult(requested, drawn, requested - drawn, o2Consumed, co2Produced, co2Removed, leaked);
    }

    public void SetLeakPctPerHour(double pctPerHour) => LeakPctPerHour = Math.Max(0, Finite.Require(pctPerHour, nameof(pctPerHour)));

    /// <summary>Installs a fresh canister (inventory bookkeeping belongs to the crew task that does it).</summary>
    public void ReplaceCanister()
    {
        LiOhRemainingKgCo2 = Spec.LiOhCapacityKgCo2;
        CanistersReplaced++;
    }

    public void SetLiOhPct(double pct) => LiOhRemainingKgCo2 = Math.Clamp(Finite.Require(pct, nameof(pct)), 0, 100) / 100.0 * Spec.LiOhCapacityKgCo2;

    public void SetPpO2Mmhg(double mmhg) => O2Kg = MassFromMmhg(Math.Max(0, Finite.Require(mmhg, nameof(mmhg))), GasConstants.MolarMassO2KgPerMol);

    public void SetPpCo2Mmhg(double mmhg) => Co2Kg = MassFromMmhg(Math.Max(0, Finite.Require(mmhg, nameof(mmhg))), GasConstants.MolarMassCo2KgPerMol);

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Spec.Id);
        writer.Write(O2Kg);
        writer.Write(Co2Kg);
        writer.Write(InertKg);
        writer.Write(_tempK);
        writer.Write(LeakPctPerHour);
        writer.Write(LiOhRemainingKgCo2);
        writer.Write((long)CanistersReplaced);
    }

    internal static double KpaFromMmhg(double mmhg) => mmhg * GasConstants.PaPerMmhg / 1000.0;

    private double MmhgOf(double massKg, double molarMassKgPerMol) =>
        massKg / molarMassKgPerMol * GasConstants.UniversalGasConstantJPerMolK * _tempK / Spec.VolumeM3 / GasConstants.PaPerMmhg;

    private double MassFromMmhg(double mmhg, double molarMassKgPerMol) =>
        mmhg * GasConstants.PaPerMmhg * Spec.VolumeM3 * molarMassKgPerMol / (GasConstants.UniversalGasConstantJPerMolK * _tempK);
}
