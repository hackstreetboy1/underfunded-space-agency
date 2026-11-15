using Usa.Simulation.LifeSupport.Atmosphere;
using Usa.Simulation.LifeSupport.Consumables;
using Usa.Simulation.LifeSupport.Thermal;

namespace Usa.Simulation.LifeSupport;

/// <summary>
/// Preallocated per-step exchange buffer for <see cref="LifeSupportSystem"/>. Crew spans use the
/// <see cref="Provisions"/> crew order; cabin spans use <see cref="LifeSupportSystem.Cabins"/> order; thermal spans
/// use <see cref="ThermalNetwork"/> node and radiator order.
/// </summary>
public sealed class LifeSupportFrame
{
    private readonly CrewActivity[] _crewActivities;
    private readonly int[] _crewCabin;
    private readonly bool[] _crewAlive;
    private readonly double[] _fanAirflowFraction;
    private readonly double[] _o2SupplyAvailableKg;
    private readonly bool[] _regulatorEnabled;
    private readonly CabinStepResult[] _cabinResults;

    internal LifeSupportFrame(ThermalInputs thermal, int cabinCount, int crewCount)
    {
        Thermal = thermal;
        _crewActivities = Enumerable.Repeat(CrewActivity.Nominal, crewCount).ToArray();
        _crewCabin = Enumerable.Repeat(cabinCount > 0 ? 0 : -1, crewCount).ToArray();
        _crewAlive = Enumerable.Repeat(true, crewCount).ToArray();
        _fanAirflowFraction = Enumerable.Repeat(1.0, cabinCount).ToArray();
        _o2SupplyAvailableKg = new double[cabinCount];
        _regulatorEnabled = Enumerable.Repeat(true, cabinCount).ToArray();
        _cabinResults = new CabinStepResult[cabinCount];
    }

    public ThermalInputs Thermal { get; }

    public Span<CrewActivity> CrewActivities => _crewActivities;

    /// <summary>Cabin index each crew member breathes from; -1 when outside every cabin (EVA on a suit loop).</summary>
    public Span<int> CrewCabin => _crewCabin;

    /// <summary>False for a dead crew member: no metabolism, no eating or drinking.</summary>
    public Span<bool> CrewAlive => _crewAlive;

    public Span<double> FanAirflowFraction => _fanAirflowFraction;

    public Span<double> O2SupplyAvailableKg => _o2SupplyAvailableKg;

    public Span<bool> RegulatorEnabled => _regulatorEnabled;

    /// <summary>Potable water produced this step (fuel cells), litres.</summary>
    public double WaterIncomeL { get; set; }

    /// <summary>Output: per-cabin mass flows of the last step.</summary>
    public ReadOnlySpan<CabinStepResult> CabinResults => _cabinResults;

    /// <summary>Output: provisions flows of the last step.</summary>
    public ProvisionsStepResult ProvisionsResult { get; internal set; }

    internal Span<CabinStepResult> WritableCabinResults => _cabinResults;
}
