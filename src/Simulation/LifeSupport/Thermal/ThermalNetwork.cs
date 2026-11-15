using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Thermal;

/// <summary>
/// Lumped-node thermal model, <c>C dT/dt = Σ Q_in − Σ Q_out</c>: load heat, heaters, crew heat, solar absorption
/// (see <see cref="ThermalInputs"/>; each node also has a persistent solar factor, 1 by default, for sunshades and tuning),
/// conduction links <c>k(T_a − T_b)</c> and radiators <c>εσA·effectiveness·(T⁴ − T_env⁴)</c>.
/// Explicit Euler, sub-stepped so each sub-step stays below a fraction of the smallest node time constant.
/// Nodes and radiators are indexed in ordinal id order.
/// </summary>
public sealed class ThermalNetwork : IHashContributor
{
    private const double MinTempK = 1.0;

    private readonly ThermalSolverConfig _solver;
    private readonly ComponentId[] _nodeIds;
    private readonly double[] _capacityJPerK;
    private readonly double[] _safeMinC;
    private readonly double[] _safeMaxC;
    private readonly double[] _solarPeakW;
    private readonly NodeSolarSpec?[] _solar;
    private readonly double[] _solarFactor;
    private readonly double[] _solarW;
    private readonly double[] _tempK;
    private readonly double[] _startTempK;
    private readonly double[] _trendCPerMin;
    private readonly double[] _extraHeatW;
    private readonly bool[] _attached;
    private readonly double[] _baseHeatW;
    private readonly double[] _netHeatW;
    private readonly double[] _conductanceSumWPerK;
    private readonly double[] _heatW;
    private readonly ThermalBand[] _bands;

    private readonly int[] _linkA;
    private readonly int[] _linkB;
    private readonly double[] _linkWPerK;

    private readonly ComponentId[] _radiatorIds;
    private readonly int[] _radiatorNode;
    private readonly double[] _radiatorEmissivityAreaM2;
    private readonly double[] _radiatorConditionFactor;
    private readonly double[] _radiatorActiveEpsilonAreaM2;
    private readonly double[] _radiatorRejectionW;

    public ThermalNetwork(ThermalNetworkSpec spec, ThermalSolverConfig solver)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(solver);
        var errors = new DefinitionErrors();
        spec.Validate(errors, "thermal network");
        errors.ThrowIfAny();
        _solver = solver;

        var nodes = spec.Nodes.OrderBy(n => n.Id).ToArray();
        _nodeIds = nodes.Select(n => n.Id).ToArray();
        _capacityJPerK = nodes.Select(n => n.HeatCapacityJPerK).ToArray();
        _safeMinC = nodes.Select(n => n.SafeMinC).ToArray();
        _safeMaxC = nodes.Select(n => n.SafeMaxC).ToArray();
        _solarPeakW = nodes.Select(n => n.SolarAbsorbedPeakW).ToArray();
        _solar = nodes.Select(n => n.Solar).ToArray();
        _solarFactor = Enumerable.Repeat(1.0, nodes.Length).ToArray();
        _solarW = new double[nodes.Length];
        _tempK = nodes.Select(n => n.InitialTempC + PhysicalConstants.ZeroCelsiusInKelvin).ToArray();
        _startTempK = new double[nodes.Length];
        _trendCPerMin = new double[nodes.Length];
        _extraHeatW = new double[nodes.Length];
        _attached = Enumerable.Repeat(true, nodes.Length).ToArray();
        _baseHeatW = new double[nodes.Length];
        _netHeatW = new double[nodes.Length];
        _conductanceSumWPerK = new double[nodes.Length];
        _heatW = new double[nodes.Length];
        _bands = new ThermalBand[nodes.Length];

        var links = spec.Links
            .OrderBy(l => l.NodeA).ThenBy(l => l.NodeB).ThenBy(l => l.ConductanceWPerK)
            .ToArray();
        _linkA = links.Select(l => IndexOfNode(l.NodeA)).ToArray();
        _linkB = links.Select(l => IndexOfNode(l.NodeB)).ToArray();
        _linkWPerK = links.Select(l => l.ConductanceWPerK).ToArray();

        var radiators = spec.Radiators.OrderBy(r => r.Id).ToArray();
        _radiatorIds = radiators.Select(r => r.Id).ToArray();
        _radiatorNode = radiators.Select(r => IndexOfNode(r.Node)).ToArray();
        _radiatorEmissivityAreaM2 = radiators.Select(r => r.Emissivity * r.AreaM2).ToArray();
        _radiatorConditionFactor = Enumerable.Repeat(1.0, radiators.Length).ToArray();
        _radiatorActiveEpsilonAreaM2 = new double[radiators.Length];
        _radiatorRejectionW = new double[radiators.Length];

        for (var i = 0; i < _nodeIds.Length; i++) _bands[i] = BandOf(i);
    }

    public int NodeCount => _nodeIds.Length;

    public int RadiatorCount => _radiatorIds.Length;

    /// <summary>Sub-steps used by the last <see cref="Step"/> (1 when the network is far from its stability limit).</summary>
    public int LastSubsteps { get; private set; }

    /// <summary>Σ C·T over attached nodes; changes only through heat crossing the network boundary.</summary>
    public double StoredEnergyJ
    {
        get
        {
            var total = 0.0;
            for (var i = 0; i < _nodeIds.Length; i++)
            {
                if (_attached[i]) total += _capacityJPerK[i] * _tempK[i];
            }
            return total;
        }
    }


    public ComponentId NodeId(int node) => _nodeIds[node];

    public ComponentId RadiatorId(int radiator) => _radiatorIds[radiator];

    /// <returns>The node index, or -1.</returns>
    public int IndexOfNode(ComponentId id)
    {
        var index = Array.BinarySearch(_nodeIds, id);
        return index >= 0 ? index : -1;
    }

    /// <returns>The radiator index, or -1.</returns>
    public int IndexOfRadiator(ComponentId id)
    {
        var index = Array.BinarySearch(_radiatorIds, id);
        return index >= 0 ? index : -1;
    }

    public double TemperatureC(int node) => _tempK[node] - PhysicalConstants.ZeroCelsiusInKelvin;

    public double TemperatureK(int node) => _tempK[node];

    /// <summary>Average rate of change over the last step.</summary>
    public double TrendCPerMin(int node) => _trendCPerMin[node];

    /// <summary>Average net heat flow into the node over the last step.</summary>
    public double NetHeatW(int node) => _netHeatW[node];

    public double SafeMinC(int node) => _safeMinC[node];

    public double SafeMaxC(int node) => _safeMaxC[node];

    public double HeatCapacityJPerK(int node) => _capacityJPerK[node];

    public bool IsAttached(int node) => _attached[node];

    public ThermalBand Band(int node) => _bands[node];

    /// <summary>Persistent extra heat (incident effects such as a shorted driver dumping heat).</summary>
    public double ExtraHeatW(int node) => _extraHeatW[node];

    /// <summary>Attitude dependence of the node's solar heating, or null.</summary>
    public NodeSolarSpec? Solar(int node) => _solar[node];

    /// <summary>Persistent multiplier (≥ 0, default 1) on the node's solar heat: a deployed sunshade, difficulty scaling.</summary>
    public double SolarFactor(int node) => _solarFactor[node];

    /// <summary>Solar heat absorbed by the node over the last step.</summary>
    public double SolarHeatW(int node) => _solarW[node];

    /// <summary>
    /// Seconds until the node leaves its safe range at the current trend: 0 when already outside,
    /// <see cref="double.PositiveInfinity"/> when stable or moving back toward the middle.
    /// </summary>
    public double TimeToWarningS(int node)
    {
        var tempC = TemperatureC(node);
        if (tempC < _safeMinC[node] || tempC > _safeMaxC[node]) return 0;
        var trend = _trendCPerMin[node];
        if (trend > 0) return (_safeMaxC[node] - tempC) / trend * 60.0;
        if (trend < 0) return (tempC - _safeMinC[node]) / -trend * 60.0;
        return double.PositiveInfinity;
    }

    /// <summary>Average heat rejected by the radiator over the last step (negative when it absorbs).</summary>
    public double RadiatorRejectionW(int radiator) => _radiatorRejectionW[radiator];

    /// <summary>Persistent degradation factor (0-1) from effects, multiplied with the per-step effectiveness input.</summary>
    public double RadiatorConditionFactor(int radiator) => _radiatorConditionFactor[radiator];

    public ThermalInputs CreateInputs() => new(_nodeIds.Length, _radiatorIds.Length, _solver.DefaultSinkTempK);

    public void SetTemperatureC(int node, double tempC) =>
        _tempK[node] = Math.Max(MinTempK, Finite.Require(tempC, nameof(tempC)) + PhysicalConstants.ZeroCelsiusInKelvin);

    public void SetExtraHeatW(int node, double heatW) => _extraHeatW[node] = Finite.Require(heatW, nameof(heatW));

    public void SetSolarFactor(int node, double factor) => _solarFactor[node] = Math.Max(0.0, Finite.Require(factor, nameof(factor)));

    public void SetRadiatorConditionFactor(int radiator, double factor) =>
        _radiatorConditionFactor[radiator] = Math.Clamp(Finite.Require(factor, nameof(factor)), 0.0, 1.0);

    /// <summary>
    /// Removes a node from the network (its section was jettisoned): it no longer exchanges heat and its
    /// temperature freezes. Radiators on it stop rejecting.
    /// </summary>
    public void Detach(int node)
    {
        _attached[node] = false;
        _trendCPerMin[node] = 0;
        _netHeatW[node] = 0;
    }

    public void Step(double deltaSeconds, ThermalInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (deltaSeconds <= 0) return;

        var nodeCount = _nodeIds.Length;
        var sunlit = Math.Clamp(Finite.Require(inputs.SunlitFraction, nameof(inputs.SunlitFraction)), 0.0, 1.0);
        Finite.Require(inputs.SinkTempK, nameof(inputs.SinkTempK));
        var load = inputs.LoadHeatW;
        var heater = inputs.HeaterW;
        var crew = inputs.CrewHeatW;
        var effectiveness = inputs.RadiatorEffectiveness;
        RequireFinite(load, nameof(inputs.LoadHeatW));
        RequireFinite(heater, nameof(inputs.HeaterW));
        RequireFinite(crew, nameof(inputs.CrewHeatW));
        RequireFinite(effectiveness, nameof(inputs.RadiatorEffectiveness));
        var exposure = inputs.SolarExposure;
        var absorbed = inputs.SolarAbsorbedW;
        RequireFinite(exposure, nameof(inputs.SolarExposure));
        RequireFinite(absorbed, nameof(inputs.SolarAbsorbedW));
        var nodeSunlit = inputs.NodeSunlitFraction;
        RequireFinite(nodeSunlit, nameof(inputs.NodeSunlitFraction));

        for (var i = 0; i < nodeCount; i++)
        {
            _startTempK[i] = _tempK[i];
            _conductanceSumWPerK[i] = 0;
            _netHeatW[i] = 0;
            _solarW[i] = _attached[i]
                ? sunlit * Math.Clamp(nodeSunlit[i], 0.0, 1.0) * _solarFactor[i] * (_solarPeakW[i] * Math.Max(0.0, exposure[i]) + Math.Max(0.0, absorbed[i]))
                : 0;
            _baseHeatW[i] = _attached[i] ? load[i] + heater[i] + crew[i] + _extraHeatW[i] + _solarW[i] : 0;
        }

        for (var l = 0; l < _linkA.Length; l++)
        {
            if (!_attached[_linkA[l]] || !_attached[_linkB[l]]) continue;
            _conductanceSumWPerK[_linkA[l]] += _linkWPerK[l];
            _conductanceSumWPerK[_linkB[l]] += _linkWPerK[l];
        }

        for (var r = 0; r < _radiatorIds.Length; r++)
        {
            var node = _radiatorNode[r];
            var active = _attached[node]
                ? Math.Clamp(effectiveness[r], 0.0, 1.0) * _radiatorConditionFactor[r] * _radiatorEmissivityAreaM2[r]
                : 0.0;
            _radiatorActiveEpsilonAreaM2[r] = active;
            _radiatorRejectionW[r] = 0;
            var t = _tempK[node];
            // Linearised radiative conductance d(σεA T⁴)/dT, for the stability guard only.
            _conductanceSumWPerK[node] += 4.0 * PhysicalConstants.StefanBoltzmann * active * t * t * t;
        }

        var substeps = SubstepsFor(deltaSeconds);
        LastSubsteps = substeps;
        var h = deltaSeconds / substeps;
        var sink = inputs.SinkTempK;
        var sink4 = sink * sink * sink * sink;

        for (var s = 0; s < substeps; s++)
        {
            var q = _heatW;
            for (var i = 0; i < nodeCount; i++) q[i] = _baseHeatW[i];

            for (var l = 0; l < _linkA.Length; l++)
            {
                int a = _linkA[l], b = _linkB[l];
                if (!_attached[a] || !_attached[b]) continue;
                var flowW = _linkWPerK[l] * (_tempK[a] - _tempK[b]);
                q[a] -= flowW;
                q[b] += flowW;
            }

            for (var r = 0; r < _radiatorIds.Length; r++)
            {
                var active = _radiatorActiveEpsilonAreaM2[r];
                if (active == 0) continue;
                var node = _radiatorNode[r];
                var t = _tempK[node];
                var rejectedW = PhysicalConstants.StefanBoltzmann * active * (t * t * t * t - sink4);
                q[node] -= rejectedW;
                _radiatorRejectionW[r] += rejectedW;
            }

            for (var i = 0; i < nodeCount; i++)
            {
                if (!_attached[i]) continue;
                _netHeatW[i] += q[i];
                _tempK[i] = Math.Max(MinTempK, _tempK[i] + q[i] * h / _capacityJPerK[i]);
            }
        }

        for (var r = 0; r < _radiatorIds.Length; r++) _radiatorRejectionW[r] /= substeps;

        for (var i = 0; i < nodeCount; i++)
        {
            _netHeatW[i] /= substeps;
            _trendCPerMin[i] = _attached[i] ? (_tempK[i] - _startTempK[i]) / deltaSeconds * 60.0 : 0;
            _bands[i] = BandOf(i);
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _nodeIds.Length; i++)
        {
            writer.Write(_nodeIds[i].Value);
            writer.Write(_tempK[i]);
            writer.Write(_attached[i]);
            writer.Write(_extraHeatW[i]);
            writer.Write(_solarFactor[i]);
        }
        for (var r = 0; r < _radiatorIds.Length; r++)
        {
            writer.Write(_radiatorIds[r].Value);
            writer.Write(_radiatorConditionFactor[r]);
        }
    }

    private static void RequireFinite(ReadOnlySpan<double> values, string name)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i]))
                throw new ArgumentOutOfRangeException(name, values[i], $"{name}[{i}] must be a finite number.");
        }
    }

    private int SubstepsFor(double deltaSeconds)
    {
        var minTauS = double.PositiveInfinity;
        for (var i = 0; i < _nodeIds.Length; i++)
        {
            if (!_attached[i] || _conductanceSumWPerK[i] <= 0) continue;
            minTauS = Math.Min(minTauS, _capacityJPerK[i] / _conductanceSumWPerK[i]);
        }
        if (double.IsPositiveInfinity(minTauS)) return 1;
        var maxStepS = _solver.StabilityFraction * minTauS;
        var needed = Math.Ceiling(deltaSeconds / maxStepS);
        // NaN (a corrupted node) must not yield zero sub-steps, which would silently stop the whole network.
        if (!(needed >= 1)) return 1;
        return (int)Math.Min(needed, _solver.MaxSubsteps);
    }

    private ThermalBand BandOf(int node)
    {
        var tempC = TemperatureC(node);
        if (tempC < _safeMinC[node]) return ThermalBand.BelowSafe;
        return tempC > _safeMaxC[node] ? ThermalBand.AboveSafe : ThermalBand.Nominal;
    }
}
