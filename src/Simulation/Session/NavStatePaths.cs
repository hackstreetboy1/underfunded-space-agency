using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// The ground's navigation picture (X8 stage 2), read-only under <c>nav.&lt;vehicle&gt;.</c> for the mission's flight vehicles:
/// <c>estimated</c> (bool: a commanded burn applied since the last fix), <c>fix_met_s</c> and <c>next_fix_met_s</c> (−1 when
/// none), <c>fix_station</c> (text, <c>''</c> for none or the launch fix), <c>sigma_along_m</c> and <c>sigma_radial_m</c> (1σ,
/// difficulty-scaled), <c>periapsis_km</c>, <c>apoapsis_km</c>, and the ground mass band <c>mass_low_kg</c>, <c>mass_high_kg</c>
/// (the stack while docked). A vehicle the ground does not track (a docked target) reads −1 numbers, <c>false</c> and <c>''</c>.
/// Values are computed on demand and cached for the current MET, so repeated reads in one step allocate nothing.
/// </summary>
internal sealed class NavStatePaths : IStateProvider
{
    public const string PathPrefix = "nav.";

    private readonly GroundTracking _tracking;
    private readonly IGroundMassSource _masses;
    private readonly Func<MissionTime> _now;
    private readonly string[] _vehicles;
    private readonly Entry[] _cache;

    public NavStatePaths(GroundTracking tracking, IGroundMassSource masses, IReadOnlyList<string> vehicles, Func<MissionTime> now)
    {
        _tracking = tracking;
        _masses = masses;
        _now = now;
        _vehicles = [.. vehicles];
        _cache = new Entry[_vehicles.Length];
        for (var i = 0; i < _cache.Length; i++) _cache[i] = new Entry();
    }

    public string Prefix => PathPrefix;

    public bool IsKnownPath(string path) => Locate(path, out _, out var field) && field != Field.None;

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        if (!Locate(path, out var vehicle, out var field) || field == Field.None) return false;
        var entry = Refresh(vehicle);
        var nav = entry.Estimate;
        value = field switch
        {
            Field.Estimated => nav?.Estimated ?? false,
            Field.FixMetS => nav?.FixAt is { } fix ? fix.Seconds : -1.0,
            Field.FixStation => nav?.FixStationId ?? "",
            Field.NextFixMetS => nav?.NextFixAt is { } next ? next.Seconds : -1.0,
            Field.SigmaAlongM => nav?.SigmaAlongM ?? -1.0,
            Field.SigmaRadialM => nav?.SigmaRadialM ?? -1.0,
            Field.PeriapsisKm => nav?.PeriapsisKm ?? -1.0,
            Field.ApoapsisKm => nav?.ApoapsisKm ?? -1.0,
            Field.MassLowKg => entry.HasMass ? entry.MassLowKg : -1.0,
            _ => entry.HasMass ? entry.MassHighKg : -1.0,
        };
        return true;
    }

    private Entry Refresh(int vehicle)
    {
        var now = _now();
        var entry = _cache[vehicle];
        if (entry.Valid && entry.At == now) return entry;
        entry.Valid = true;
        entry.At = now;
        entry.Estimate = _tracking.Estimate(_vehicles[vehicle], now);
        entry.HasMass = _masses.TryGetMass(_vehicles[vehicle], out entry.MassLowKg, out _, out entry.MassHighKg);
        return entry;
    }

    private bool Locate(string path, out int vehicle, out Field field)
    {
        vehicle = -1;
        field = Field.None;
        if (path is null || !path.StartsWith(PathPrefix, StringComparison.Ordinal)) return false;
        var rest = path.AsSpan(PathPrefix.Length);
        var dot = rest.LastIndexOf('.');
        if (dot <= 0) return false;
        var id = rest[..dot];
        for (var i = 0; i < _vehicles.Length; i++)
        {
            if (id.SequenceEqual(_vehicles[i])) vehicle = i;
        }
        if (vehicle < 0) return false;
        field = rest[(dot + 1)..] switch
        {
            "estimated" => Field.Estimated,
            "fix_met_s" => Field.FixMetS,
            "fix_station" => Field.FixStation,
            "next_fix_met_s" => Field.NextFixMetS,
            "sigma_along_m" => Field.SigmaAlongM,
            "sigma_radial_m" => Field.SigmaRadialM,
            "periapsis_km" => Field.PeriapsisKm,
            "apoapsis_km" => Field.ApoapsisKm,
            "mass_low_kg" => Field.MassLowKg,
            "mass_high_kg" => Field.MassHighKg,
            _ => Field.None,
        };
        return true;
    }

    private enum Field
    {
        None,
        Estimated,
        FixMetS,
        FixStation,
        NextFixMetS,
        SigmaAlongM,
        SigmaRadialM,
        PeriapsisKm,
        ApoapsisKm,
        MassLowKg,
        MassHighKg,
    }

    private sealed class Entry
    {
        public bool Valid;
        public MissionTime At;
        public NavEstimate? Estimate;
        public bool HasMass;
        public double MassLowKg;
        public double MassHighKg;
    }
}
