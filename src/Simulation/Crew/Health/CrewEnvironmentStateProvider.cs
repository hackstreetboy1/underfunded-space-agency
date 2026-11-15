using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Health;

/// <summary>
/// Serves what one crew member is breathing and feeling right now, from the place they are in:
/// <c>crew.&lt;id&gt;.env.ppco2_mmhg</c>, <c>env.ppo2_kpa</c>, <c>env.pressure_kpa</c>, <c>env.temp_c</c> (suit values on EVA)
/// and <c>env.rate_dps</c>. Register one per member next to <see cref="CrewRoster"/>; its longer prefix wins.
/// </summary>
/// <remarks>
/// Reads the same <see cref="ICrewExposureSource"/> the health model uses, so <c>config/health.json</c> must not point its
/// exposure paths at <c>crew.{crew}.env.*</c> (that would read itself).
/// </remarks>
public sealed class CrewEnvironmentStateProvider : IStateProvider
{
    public const string Segment = "env.";

    private readonly CrewId _crew;
    private readonly ICrewExposureSource _exposures;

    public CrewEnvironmentStateProvider(CrewId crew, ICrewExposureSource exposures)
    {
        _crew = crew;
        _exposures = exposures ?? throw new ArgumentNullException(nameof(exposures));
        Prefix = $"{StatePaths.Crew}{crew.Value}.{Segment}";
    }

    public string Prefix { get; }

    /// <summary>One provider per roster member, in id order.</summary>
    public static CrewEnvironmentStateProvider[] ForRoster(CrewRoster roster, ICrewExposureSource exposures)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var providers = new CrewEnvironmentStateProvider[roster.Members.Count];
        for (var i = 0; i < providers.Length; i++) providers[i] = new CrewEnvironmentStateProvider(roster.Members[i].Id, exposures);
        return providers;
    }

    public bool IsKnownPath(string path) => Field(path) is not null;

    public bool TryGet(string path, out StateValue value)
    {
        var field = Field(path);
        if (field is null)
        {
            value = StateValue.None;
            return false;
        }
        var exposure = _exposures.Read(_crew);
        value = field switch
        {
            "ppco2_mmhg" => exposure.Ppco2Mmhg,
            "ppo2_kpa" => exposure.Ppo2Kpa,
            "pressure_kpa" => exposure.PressureKpa,
            "temp_c" => exposure.TemperatureC,
            _ => exposure.AngularRateDps,
        };
        return true;
    }

    private string? Field(string path)
    {
        if (path is null || !path.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        return path.AsSpan(Prefix.Length) switch
        {
            "ppco2_mmhg" => "ppco2_mmhg",
            "ppo2_kpa" => "ppo2_kpa",
            "pressure_kpa" => "pressure_kpa",
            "temp_c" => "temp_c",
            "rate_dps" => "rate_dps",
            _ => null,
        };
    }
}
