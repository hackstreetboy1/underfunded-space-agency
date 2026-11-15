using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Cues;
using Usa.Simulation.Flight;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// Builds the frame's <see cref="AudioStateSample"/> for <see cref="AudioCueMapper.Sample"/>: entry heating from the tracked
/// heating proxy, powered flight, docking, and attitude-thruster pulses (a thruster starting to fire since the previous frame).
/// Pulses count only thrusters whose plume the external view may show (<see cref="ThrusterPlumeGate"/>, GDD 4.4 and 14), so
/// sound never reports a thruster the player has not confirmed. Pulses shorter than a frame under heavy time compression are
/// not seen; the mapper caps pulse sounds anyway.
/// </summary>
public sealed class AudioStateSampler
{
    private const double FiringEpsilon = 1e-3;

    private readonly FlightTrack _track;
    private readonly ThrusterPlumeGate _plumes;
    private readonly double _peakHeatingProxy;
    private readonly int[] _thrusters;
    private readonly bool[] _wasFiring;

    public AudioStateSampler(MissionSession session, FlightTrack track, SpacecraftViewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(track);
        _track = track;
        _peakHeatingProxy = (options ?? SpacecraftViewOptions.Default).PeakHeatingProxy;
        _plumes = new ThrusterPlumeGate(session.Data, session.State);

        var thrusters = new List<int>();
        for (var i = 0; i < _plumes.ComponentIds.Count; i++)
        {
            if (_plumes.IsAttitudeThruster(i)) thrusters.Add(i);
        }
        _thrusters = [.. thrusters];
        _wasFiring = new bool[_thrusters.Length];
        for (var i = 0; i < _thrusters.Length; i++) _wasFiring[i] = Firing(i);
    }

    /// <summary>
    /// The external view is the active main view (GDD 14: thruster pulses are heard on the external view). While false no pulse
    /// is reported; firing is still tracked, so switching back does not replay pulses that started meanwhile. Defaults to true.
    /// </summary>
    public bool ExternalViewVisible { get; set; } = true;

    /// <summary>Call once per frame, after <see cref="FlightTrack.Update"/>.</summary>
    public AudioStateSample Sample()
    {
        var pulses = 0;
        for (var i = 0; i < _thrusters.Length; i++)
        {
            var firing = Firing(i);
            if (firing && !_wasFiring[i] && ExternalViewVisible) pulses++;
            _wasFiring[i] = firing;
        }

        return new AudioStateSample(
            Math.Clamp(_track.HeatingProxy / _peakHeatingProxy, 0, 1),
            _track.Phase == FlightPhase.Powered,
            _track.Docked,
            pulses);
    }

    private bool Firing(int thruster) => _plumes.VisibleFraction(_thrusters[thruster]) > FiringEpsilon;
}
