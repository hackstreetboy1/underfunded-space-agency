namespace Usa.Simulation.LifeSupport.Thermal;

/// <summary>
/// Per-step heat inputs for a <see cref="ThermalNetwork"/>, indexed like its nodes and radiators.
/// Values persist between steps until overwritten. Create with <see cref="ThermalNetwork.CreateInputs"/>.
/// </summary>
/// <remarks>
/// A node's solar heat is <c>SunlitFraction × NodeSunlitFraction × solar factor × (SolarAbsorbedPeakW × SolarExposure + SolarAbsorbedW)</c>;
/// with the defaults (node sunlit 1, exposure 1, absorbed 0, factor 1) that is the node's peak scaled by <see cref="SunlitFraction"/>.
/// </remarks>
public sealed class ThermalInputs
{
    private readonly double[] _loadHeatW;
    private readonly double[] _heaterW;
    private readonly double[] _crewHeatW;
    private readonly double[] _radiatorEffectiveness;
    private readonly double[] _solarExposure;
    private readonly double[] _solarAbsorbedW;
    private readonly double[] _nodeSunlitFraction;

    internal ThermalInputs(int nodeCount, int radiatorCount, double sinkTempK)
    {
        _loadHeatW = new double[nodeCount];
        _heaterW = new double[nodeCount];
        _crewHeatW = new double[nodeCount];
        _radiatorEffectiveness = new double[radiatorCount];
        _radiatorEffectiveness.AsSpan().Fill(1.0);
        _solarExposure = new double[nodeCount];
        _solarExposure.AsSpan().Fill(1.0);
        _solarAbsorbedW = new double[nodeCount];
        _nodeSunlitFraction = new double[nodeCount];
        _nodeSunlitFraction.AsSpan().Fill(1.0);
        SinkTempK = sinkTempK;
    }

    /// <summary>Waste heat of powered electrical loads dissipating into each node.</summary>
    public Span<double> LoadHeatW => _loadHeatW;

    /// <summary>Electrical heater output per node (zero when unpowered or switched off).</summary>
    public Span<double> HeaterW => _heaterW;

    /// <summary>Crew body heat per node; written by <see cref="LifeSupportSystem"/> from the metabolic model.</summary>
    public Span<double> CrewHeatW => _crewHeatW;

    /// <summary>0-1 per radiator: orientation to sun/Earth and loop condition, supplied by other areas.</summary>
    public Span<double> RadiatorEffectiveness => _radiatorEffectiveness;

    /// <summary>
    /// Per node, the share (≥ 0, normally 0-1) of its <see cref="ThermalNodeSpec.SolarAbsorbedPeakW"/> it receives in full
    /// sun at the current attitude: sun-pitch curve, incidence or shading. Default 1. See <see cref="SolarHeating"/>.
    /// </summary>
    public Span<double> SolarExposure => _solarExposure;

    /// <summary>
    /// Per node, solar heat (W, ≥ 0) its sunlit surfaces absorb in full sun at the current attitude, on top of the peak
    /// path. Default 0. See <see cref="SolarHeating.AbsorbedW"/>.
    /// </summary>
    public Span<double> SolarAbsorbedW => _solarAbsorbedW;

    /// <summary>0 in eclipse, 1 in full sun (penumbra between); scales every node's solar heat.</summary>
    public double SunlitFraction { get; set; }

    /// <summary>
    /// Per node, 0-1: the sunlit fraction of the node's own vehicle, on top of <see cref="SunlitFraction"/> (default 1). Vehicles
    /// flying apart can be on different sides of the shadow edge (review SIM-2 S18).
    /// </summary>
    public Span<double> NodeSunlitFraction => _nodeSunlitFraction;

    /// <summary>Effective radiative environment temperature seen by the radiators.</summary>
    public double SinkTempK { get; set; }
}
