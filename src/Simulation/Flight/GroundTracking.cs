using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// What the ground knows of the orbits (X8 stage 2). Each station is a longitude on the rotating rim; the vehicle is in a
/// pass while it is above the station's minimum elevation. After <see cref="NavigationConfig.FixAfterTrackS"/> of
/// coasting track in one pass the ground gets a fix: truth plus a seeded error (stream <see cref="RngStream"/>, clamped
/// to <see cref="NavigationConfig.ClampSigma"/>). Between fixes the estimate coasts on, and each commanded burn is flown
/// on it at the ledger mass; uncommanded thrust (a stuck thruster, attitude error) never reaches it. σ grows linearly:
/// per orbit since the fix, plus the along-track drift and radial offset each burn's execution and pointing errors cause
/// (Clohessy–Wiltshire: 3·δv per second along, 2·δv/n radial). Reads truth only to detect passes and fix; writes nothing
/// the simulation reads back, and draws only from its own stream, so truth runs identically with or without it.
/// </summary>
public sealed class GroundTracking : ISimSystem, IHashContributor
{
    public const string RngStream = "nav";

    /// <summary>Burns shorter than this are not worth a σ entry (attitude pulses).</summary>
    private const double NegligibleDeltaVMps = 1e-3;

    private readonly FlightSystem _flight;
    private readonly NavigationConfig _config;
    private readonly double _scale;
    private readonly IGroundMassSource? _masses;
    private readonly Func<string, bool>? _attitudeHeld;
    private readonly Pcg32 _random;
    private readonly RecoveryZones _zones;
    private readonly IReadOnlyList<TrackingStation> _stations;
    private readonly double[] _stationLongitudeRad;
    private readonly double _minElevationRad;
    private readonly Track _crewed;
    private Track? _target;
    private ProgrammedBurn? _lastCommanded;

    /// <param name="scenarioSeed">Seeds the <see cref="RngStream"/> stream.</param>
    /// <param name="uncertaintyScale">The difficulty's <c>nav_uncertainty_scale</c>: scales every σ (Easy 0.5, Normal 1).</param>
    /// <param name="masses">The ground mass ledger; without it commanded burns fly at the estimate's own mass.</param>
    /// <param name="attitudeHeld">Whether attitude control holds a vehicle's burns (pointing σ 1° rather than 15°); null means always.</param>
    public GroundTracking(
        FlightSystem flight, NavigationConfig config, ulong scenarioSeed, double uncertaintyScale = 1,
        IGroundMassSource? masses = null, Func<string, bool>? attitudeHeld = null)
    {
        _flight = flight ?? throw new ArgumentNullException(nameof(flight));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (!(uncertaintyScale >= 0) || double.IsInfinity(uncertaintyScale))
            throw new ArgumentOutOfRangeException(nameof(uncertaintyScale), uncertaintyScale, "Must be zero or more.");
        _scale = uncertaintyScale;
        _masses = masses;
        _attitudeHeld = attitudeHeld;
        _random = Pcg32.ForStream(scenarioSeed, RngStream);
        var mission = flight.Mission;
        _zones = mission.Zones;
        _stations = mission.Stations;
        _stationLongitudeRad = new double[_stations.Count];
        for (var i = 0; i < _stations.Count; i++) _stationLongitudeRad[i] = _stations[i].LongitudeDeg * Angles.DegToRad;
        _minElevationRad = mission.TrackingMinElevationDeg * Angles.DegToRad;

        // Launch tracking: the ground starts with a fix at the start.
        _crewed = new Track(flight.CrewedId);
        Fix(_crewed, flight.Crewed, flight.Now, stationId: null);
        if (flight.Target is { } target)
        {
            _target = new Track(target.VehicleId);
            Fix(_target, target, flight.Now, stationId: null);
        }
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public NavigationConfig Config => _config;

    public IReadOnlyList<TrackingStation> Stations => _stations;

    public void Step(in StepContext context)
    {
        var now = context.Now;
        if (_flight.ScheduledBurn is { } commanded && !ReferenceEquals(commanded, _lastCommanded))
        {
            _lastCommanded = commanded;
            _crewed.AddCommanded(commanded.Plan);
        }

        var targetTruth = _flight.Target;
        if (targetTruth is null)
        {
            _target = null;
        }
        else if (_target is null)
        {
            // Undocked: the ground knows the target left from the stack it was tracking.
            _target = new Track(targetTruth.VehicleId);
            _target.CopyFrom(_crewed, targetTruth.VehicleId, _zones);
        }

        Update(_crewed, _flight.Crewed, now);
        if (_target is not null && targetTruth is not null) Update(_target, targetTruth, now);
    }

    /// <summary>Records a burn the ground commanded by other means than the flight's programmed burn (an OAMS burn task).</summary>
    public void RecordCommandedBurn(string vehicleId, ManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.RequireFinite();
        TrackOf(vehicleId)?.AddCommanded(plan);
    }

    /// <summary>The ground's picture of <paramref name="vehicleId"/>; null for a vehicle it doesn't track (a docked target).</summary>
    public NavEstimate? Estimate(string vehicleId, MissionTime now)
    {
        if (TrackOf(vehicleId) is not { } track) return null;
        var estimate = track.Estimate;
        var kinematics = estimate.Sample(now);
        var conic = estimate.Conic;
        var radius = estimate.Body.RadiusM;
        var (along, radial) = Sigma(track, now);
        return new NavEstimate(
            vehicleId, kinematics.Time, estimate.Phase, kinematics.State, conic,
            (conic.PeriapsisM - radius) / 1000, (conic.ApoapsisM - radius) / 1000, along, radial,
            track.FixAt, track.FixStationId, track.Estimated,
            track.FixAt is not { } fix || (now.Milliseconds - fix.Milliseconds) / 1000.0 > _config.StaleAfterS,
            NextFixAt(track, now));
    }

    /// <summary>
    /// A copy of the estimate for the planner to preview on (the map's flight picture), at the ledger mass when known;
    /// null while the ground's vehicle is burning or down.
    /// </summary>
    public OrbitPropagator? PreviewState(string vehicleId)
    {
        if (TrackOf(vehicleId) is not { } track || !ManeuverPlanner.CanPreview(track.Estimate)) return null;
        var copy = track.Estimate.Clone();
        if (_masses is not null && _masses.TryGetMass(vehicleId, out _, out var bestKg, out _) && bestKg > 0) copy.SetMass(bestKg);
        return copy;
    }

    /// <summary>The planner's uncertainty for a preview of <paramref name="vehicleId"/> now; null when it isn't tracked.</summary>
    public PreviewUncertainty? Uncertainty(string vehicleId, MissionTime now)
    {
        if (TrackOf(vehicleId) is not { } track) return null;
        var (along, radial) = Sigma(track, now);
        var mass = track.Estimate.MassKg;
        var low = mass;
        var high = mass;
        if (_masses is not null && _masses.TryGetMass(vehicleId, out var lowKg, out _, out var highKg))
        {
            low = lowKg;
            high = highKg;
        }
        return new PreviewUncertainty(radial, along, low, high, _config.DisplaySigma);
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((long)_random.State);
        _crewed.WriteHash(writer);
        writer.Write(_target is not null);
        _target?.WriteHash(writer);
    }

    private Track? TrackOf(string vehicleId) =>
        _crewed.VehicleId == vehicleId ? _crewed : _target is { } target && target.VehicleId == vehicleId ? target : null;

    private void Update(Track track, OrbitPropagator truth, MissionTime now)
    {
        Propagate(track, now);
        var inView = truth.Phase == FlightPhase.Coast ? StationInView(truth.Sample(now).State, now) : -1;
        if (inView < 0 || inView != track.PassStation)
        {
            track.PassStation = inView;
            track.PassFixed = false;
            track.TrackStart = null;
        }
        if (inView < 0 || track.PassFixed) return;
        track.TrackStart ??= now;
        if ((now.Milliseconds - track.TrackStart.Value.Milliseconds) / 1000.0 < _config.FixAfterTrackS) return;
        Fix(track, truth, now, _stations[inView].Id);
        track.PassFixed = true;
        track.TrackStart = null;
    }

    /// <summary>Flies the estimate to <paramref name="now"/>, starting and ending commanded burns on the way.</summary>
    private void Propagate(Track track, MissionTime now)
    {
        var estimate = track.Estimate;
        while (true)
        {
            if (track.Burn is { } burn)
            {
                if (estimate.Phase != FlightPhase.Powered)
                {
                    track.Burn = null;
                    continue;
                }
                if (now < burn.EndTime)
                {
                    estimate.AdvancePowered(now, burn, allowPartialStep: false);
                    return;
                }
                estimate.AdvancePowered(burn.EndTime, burn, allowPartialStep: true);
                if (estimate.Phase == FlightPhase.Powered) estimate.EndBurn();
                track.Burn = null;
                AddBurnSigma(track, estimate.BurnDeltaVMps, estimate);
                continue;
            }

            if (track.NextCommanded(now) is { } plan)
            {
                if (estimate.Phase == FlightPhase.Landed) continue;
                var start = plan.StartTime < estimate.Time ? estimate.Time : plan.StartTime;
                var mass = _masses is not null && _masses.TryGetMass(track.VehicleId, out _, out var bestKg, out _) && bestKg > 0
                    ? bestKg
                    : estimate.MassKg;
                if (estimate.BeginBurn(start, mass))
                {
                    track.Burn = new ProgrammedBurn(plan with { StartTime = start });
                    track.Estimated = true;
                }
                continue;
            }

            if (estimate.Phase != FlightPhase.Landed) estimate.Advance(now);
            return;
        }
    }

    private void AddBurnSigma(Track track, double deltaVMps, OrbitPropagator estimate)
    {
        if (!(deltaVMps > NegligibleDeltaVMps)) return;
        var pointingDeg = _attitudeHeld is null || _attitudeHeld(track.VehicleId) ? _config.PointingSigmaDeg : _config.PointingSigmaNoAttitudeDeg;
        var alongMps = _config.BurnExecutionFraction * deltaVMps * _scale;
        var radialMps = deltaVMps * Math.Sin(pointingDeg * Angles.DegToRad) * _scale;
        var conic = estimate.Conic;
        var meanMotion = conic.IsHyperbolic ? track.MeanMotionRadPerS : conic.MeanMotionRadPerS;
        var endS = estimate.Time.Milliseconds / 1000.0;
        track.AlongRateMps += 3 * alongMps;
        track.AlongOffsetM += 3 * alongMps * endS;
        track.RadialAddedM += (2 * alongMps + radialMps) / meanMotion;
    }

    private (double AlongM, double RadialM) Sigma(Track track, MissionTime now)
    {
        var sinceFixS = (now.Milliseconds - track.SigmaEpoch.Milliseconds) / 1000.0;
        var orbits = sinceFixS * track.MeanMotionRadPerS / Angles.TwoPi;
        var nowS = now.Milliseconds / 1000.0;
        var along = track.BaseAlongM + _scale * _config.GrowthPerOrbit.AlongM * orbits + track.AlongRateMps * nowS - track.AlongOffsetM;
        var radial = track.BaseRadialM + _scale * _config.GrowthPerOrbit.RadialM * orbits + track.RadialAddedM;
        return (along, radial);
    }

    /// <summary>Sets the estimate to truth plus a seeded error, consistent with the true orbit's size (no false drift).</summary>
    private void Fix(Track track, OrbitPropagator truth, MissionTime now, string? stationId)
    {
        var sigmaAlong = _scale * _config.FixSigma.AlongM;
        var sigmaRadial = _scale * _config.FixSigma.RadialM;
        var alongError = Clamped(sigmaAlong);
        var radialError = Clamped(sigmaRadial);

        var conic = truth.Conic;
        var state = truth.Sample(now).State;
        if (!conic.IsHyperbolic && truth.Phase == FlightPhase.Coast)
        {
            // Along-track: the same orbit, a little ahead or behind.
            var shift = alongError / state.SpeedMps * conic.MeanMotionRadPerS;
            state = (conic with { MeanAnomalyAtEpochRad = conic.MeanAnomalyAtEpochRad + shift }).StateAt(now);
            // Radial: moved up or down at the same orbital energy, so the semi-major axis (and period) stay true.
            var radius = state.RadiusM + radialError;
            var speedSquared = conic.MuM3PerS2 * (2 / radius - 1 / conic.SemiMajorAxisM);
            if (speedSquared > 0)
                state = new StateVector(state.PositionM.Normalized() * radius, state.VelocityMps.Normalized() * Math.Sqrt(speedSquared));
        }

        var mass = _masses is not null && _masses.TryGetMass(track.VehicleId, out _, out var bestKg, out _) && bestKg > 0 ? bestKg : truth.MassKg;
        track.Estimate = new OrbitPropagator(
            track.VehicleId, truth.Body, now, state, mass, truth.BallisticCoefficientKgPerM2, _zones);
        track.Burn = null;
        track.DropCommandedBefore(now);
        track.FixAt = now;
        track.FixStationId = stationId;
        track.Estimated = false;
        track.SigmaEpoch = now;
        track.BaseAlongM = sigmaAlong;
        track.BaseRadialM = sigmaRadial;
        track.AlongRateMps = 0;
        track.AlongOffsetM = 0;
        track.RadialAddedM = 0;
        var estimateConic = track.Estimate.Conic;
        track.MeanMotionRadPerS = estimateConic.IsHyperbolic ? 0 : estimateConic.MeanMotionRadPerS;
    }

    /// <summary>A normal draw (Box–Muller, two uniforms from the stream every time) clamped to ±<see cref="NavigationConfig.ClampSigma"/> σ.</summary>
    private double Clamped(double sigma)
    {
        var u1 = 1 - _random.NextDouble();
        var u2 = _random.NextDouble();
        var normal = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(Angles.TwoPi * u2);
        return Math.Clamp(normal, -_config.ClampSigma, _config.ClampSigma) * sigma;
    }

    /// <summary>The station whose horizon circle (above the minimum elevation) holds the vehicle; -1 for none.</summary>
    private int StationInView(in StateVector state, MissionTime now)
    {
        var halfAngle = CoverageHalfAngleRad(state.RadiusM);
        if (!(halfAngle > 0)) return -1;
        var longitude = _zones.LandingLongitudeRad(state.PositionM.Angle, now);
        for (var i = 0; i < _stationLongitudeRad.Length; i++)
        {
            if (Math.Abs(Angles.Wrap(longitude - _stationLongitudeRad[i])) <= halfAngle) return i;
        }
        return -1;
    }

    /// <summary>Central angle from a station to where a vehicle at <paramref name="radiusM"/> sits at the minimum elevation.</summary>
    private double CoverageHalfAngleRad(double radiusM)
    {
        var cosine = _flight.Mission.Body.RadiusM * Math.Cos(_minElevationRad) / radiusM;
        return cosine is > 0 and < 1 ? Math.Acos(cosine) - _minElevationRad : 0;
    }

    /// <summary>
    /// The current pass's fix time, or the next pass's start plus the track time, from the ground track rate of the
    /// estimate (orbital motion minus the body's rotation); null while burning, in the atmosphere or without stations.
    /// </summary>
    private MissionTime? NextFixAt(Track track, MissionTime now)
    {
        if (track.TrackStart is { } started) return started + (long)(_config.FixAfterTrackS * 1000);
        var estimate = track.Estimate;
        if (_stationLongitudeRad.Length == 0 || estimate.Phase != FlightPhase.Coast || estimate.Conic.IsHyperbolic) return null;
        var state = estimate.Sample(now).State;
        var rateRadPerS = estimate.Conic.Direction * estimate.Conic.MeanMotionRadPerS - _zones.RotationRateRadPerS;
        var halfAngle = CoverageHalfAngleRad(estimate.Conic.SemiMajorAxisM);
        if (Math.Abs(rateRadPerS) < 1e-9 || !(halfAngle > 0)) return null;
        var longitude = _zones.LandingLongitudeRad(state.PositionM.Angle, now);
        var sense = Math.Sign(rateRadPerS);
        var soonestS = double.PositiveInfinity;
        for (var i = 0; i < _stationLongitudeRad.Length; i++)
        {
            if (i == track.PassStation) continue;
            var entry = _stationLongitudeRad[i] - sense * halfAngle;
            var ahead = Angles.WrapPositive(sense * (entry - longitude));
            soonestS = Math.Min(soonestS, ahead / Math.Abs(rateRadPerS));
        }
        return double.IsFinite(soonestS) ? now + (long)((soonestS + _config.FixAfterTrackS) * 1000) : null;
    }

    private sealed class Track(string vehicleId)
    {
        private readonly List<ManeuverPlan> _commanded = [];

        public string VehicleId { get; } = vehicleId;

        public OrbitPropagator Estimate { get; set; } = null!;

        public ProgrammedBurn? Burn { get; set; }

        public MissionTime? FixAt { get; set; }

        public string? FixStationId { get; set; }

        public bool Estimated { get; set; }

        public MissionTime SigmaEpoch { get; set; }

        public double BaseAlongM { get; set; }

        public double BaseRadialM { get; set; }

        /// <summary>Σ 3·δv over the burns since the fix: along-track σ grows by this every second.</summary>
        public double AlongRateMps { get; set; }

        /// <summary>Σ 3·δv·t_burn, so the burns' along-track σ at t is AlongRateMps·t − AlongOffsetM.</summary>
        public double AlongOffsetM { get; set; }

        public double RadialAddedM { get; set; }

        public double MeanMotionRadPerS { get; set; }

        public int PassStation { get; set; } = -1;

        public bool PassFixed { get; set; }

        public MissionTime? TrackStart { get; set; }

        public void AddCommanded(ManeuverPlan plan)
        {
            var at = _commanded.Count;
            while (at > 0 && _commanded[at - 1].StartTime > plan.StartTime) at--;
            _commanded.Insert(at, plan);
        }

        public ManeuverPlan? NextCommanded(MissionTime now)
        {
            if (_commanded.Count == 0 || _commanded[0].StartTime > now) return null;
            var plan = _commanded[0];
            _commanded.RemoveAt(0);
            return plan;
        }

        public void DropCommandedBefore(MissionTime time)
        {
            while (_commanded.Count > 0 && _commanded[0].StartTime < time) _commanded.RemoveAt(0);
        }

        public void CopyFrom(Track source, string vehicleId, RecoveryZones zones)
        {
            var estimate = source.Estimate;
            Estimate = new OrbitPropagator(vehicleId, estimate.Body, estimate.Time, estimate.State, estimate.MassKg,
                estimate.BallisticCoefficientKgPerM2, zones);
            FixAt = source.FixAt;
            FixStationId = source.FixStationId;
            Estimated = source.Estimated;
            SigmaEpoch = source.SigmaEpoch;
            BaseAlongM = source.BaseAlongM;
            BaseRadialM = source.BaseRadialM;
            AlongRateMps = source.AlongRateMps;
            AlongOffsetM = source.AlongOffsetM;
            RadialAddedM = source.RadialAddedM;
            MeanMotionRadPerS = source.MeanMotionRadPerS;
        }

        public void WriteHash(StateHashWriter writer)
        {
            writer.Write(VehicleId);
            Estimate.WriteHash(writer);
            writer.Write(Burn is not null);
            if (Burn is not null)
            {
                writer.Write(Burn.StartTime.Milliseconds);
                writer.Write(Burn.EndTime.Milliseconds);
                writer.Write(Burn.PropellantUsedKg);
            }
            writer.Write(_commanded.Count);
            for (var i = 0; i < _commanded.Count; i++) writer.Write(_commanded[i].StartTime.Milliseconds);
            writer.Write(FixAt?.Milliseconds ?? -1);
            writer.Write(FixStationId ?? string.Empty);
            writer.Write(Estimated);
            writer.Write(SigmaEpoch.Milliseconds);
            writer.Write(BaseAlongM);
            writer.Write(BaseRadialM);
            writer.Write(AlongRateMps);
            writer.Write(AlongOffsetM);
            writer.Write(RadialAddedM);
            writer.Write(MeanMotionRadPerS);
            writer.Write(PassStation);
            writer.Write(PassFixed);
            writer.Write(TrackStart?.Milliseconds ?? -1);
        }
    }
}
