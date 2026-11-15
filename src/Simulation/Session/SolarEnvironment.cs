using Usa.Simulation.Attitude;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.LifeSupport.Thermal;

namespace Usa.Simulation.Session;

/// <summary>
/// The Sun for the thermal network (M2 §16 E4/E5), from Flight's <see cref="SunModel"/> (<c>flight.json</c> <c>sun</c>, or the
/// <c>session.json</c> <c>sun</c> override). Each step every node gets the sunlit fraction of its own vehicle
/// (<see cref="ThermalInputs.NodeSunlitFraction"/>; the network-wide <see cref="ThermalInputs.SunlitFraction"/> is 1), so a
/// separated vehicle can be in shadow while the other is in sunlight, and every solar node is filled
/// (<see cref="SolarHeating.Fill"/>) with the Sun as its vehicle sees it: the body-frame direction from the attitude orientation
/// and the sun pitch <c>attitude.&lt;v&gt;.sun_pitch_deg</c>. A node on no known vehicle takes the crewed vehicle's values.
/// Everything is a pure function of this step's flight and attitude state, so nothing here needs hashing.
/// </summary>
internal sealed class SolarEnvironment
{
    private readonly SunModel _sun;
    private readonly string[] _vehicleIds;
    private readonly AttitudeVehicle?[] _attitude;
    private readonly int[] _nodeVehicle;
    private readonly Dictionary<ComponentId, int> _vehicleOfNode = [];
    private readonly double[] _sunlit;
    private readonly SunView?[] _views;
    private readonly Func<ComponentId, SunView?> _sunFor;

    public SolarEnvironment(SunModel sun, AttitudeSystem attitude, VehicleSet vehicles, ThermalNetwork network)
    {
        _sun = sun ?? throw new ArgumentNullException(nameof(sun));
        ArgumentNullException.ThrowIfNull(attitude);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(network);
        _vehicleIds = new string[vehicles.Count];
        _attitude = new AttitudeVehicle?[vehicles.Count];
        for (var v = 0; v < vehicles.Count; v++)
        {
            _vehicleIds[v] = vehicles[v].VehicleId;
            _attitude[v] = attitude.TryGetVehicle(vehicles[v].VehicleId, out var a) ? a : null;
        }
        _nodeVehicle = new int[network.NodeCount];
        for (var n = 0; n < network.NodeCount; n++)
        {
            _nodeVehicle[n] = vehicles.TryFind(network.NodeId(n).Value, out var v, out _) ? v : -1;
            if (_nodeVehicle[n] >= 0) _vehicleOfNode[network.NodeId(n)] = v;
        }
        _sunlit = new double[vehicles.Count];
        _views = new SunView?[vehicles.Count];
        _sunFor = node => _vehicleOfNode.TryGetValue(node, out var v) ? _views[v] : null;
    }

    /// <summary>The Sun as <paramref name="vehicle"/> sees it now: body-frame direction and sun pitch in degrees (−180…180].</summary>
    public static SunView ViewFrom(AttitudeVehicle vehicle, Vec3d sunInertial, double sunInertialDeg)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        var body = vehicle.Orientation.Conjugate.Rotate(sunInertial);
        return new SunView(body, PointingMath.SunPitchDeg(vehicle.PointingDeg, sunInertialDeg));
    }

    public void Fill(ThermalNetwork network, ThermalInputs inputs)
    {
        inputs.SunlitFraction = 1.0;
        for (var v = 0; v < _views.Length; v++)
        {
            _sunlit[v] = _sun.SunlitFraction(_vehicleIds[v]);
            _views[v] = _attitude[v] is { } vehicle ? ViewFrom(vehicle, _sun.SunInertial, _sun.Definition.InertialAngleDeg) : null;
        }
        var crewed = _sun.CrewedSunlitFraction();
        var nodeSunlit = inputs.NodeSunlitFraction;
        for (var n = 0; n < _nodeVehicle.Length; n++) nodeSunlit[n] = _nodeVehicle[n] >= 0 ? _sunlit[_nodeVehicle[n]] : crewed;
        SolarHeating.Fill(network, inputs, _sunFor);
    }
}
