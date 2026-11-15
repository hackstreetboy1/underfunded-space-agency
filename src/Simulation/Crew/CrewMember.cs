using Usa.Simulation.Crew.Health;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew;

/// <summary>
/// Mutable state of one astronaut. Health fields are written by <see cref="HealthModel"/>;
/// activity and location by <see cref="Tasks.CrewTaskRunner"/>.
/// </summary>
public sealed class CrewMember
{
    private readonly double[] _damageByCause = new double[HealthConfig.CauseCount];
    private readonly double[] _harmfulForS = new double[HealthConfig.CauseCount];
    private readonly CrewLocations _locations;

    internal CrewMember(CrewMemberDefinition definition, double launchHealth, HealthBand band, CrewLocations locations)
    {
        Definition = definition;
        Health = launchHealth;
        LaunchHealth = launchHealth;
        WorstHealth = launchHealth;
        Band = band;
        _locations = locations;
        LocationIndex = locations.DefaultIndex;
        HomeIndex = locations.DefaultIndex;
    }

    public CrewMemberDefinition Definition { get; }

    public CrewId Id => Definition.Id;

    /// <summary>0-100; 0 means dead.</summary>
    public double Health { get; internal set; }

    public double LaunchHealth { get; }

    public double WorstHealth { get; internal set; }

    public HealthBand Band { get; internal set; }

    public bool IsAlive => Band != HealthBand.Deceased;

    /// <summary>Where the member is now: the active task's location, otherwise <see cref="HomeLocation"/>.</summary>
    public CrewLocationDefinition CurrentLocation => _locations[LocationIndex];

    public string LocationId => CurrentLocation.Id;

    /// <summary>Where the member stays between tasks (the default location until a task or the session moves them).</summary>
    public CrewLocationDefinition HomeLocation => _locations[HomeIndex];

    /// <summary>Coarse view of <see cref="CurrentLocation"/>: <see cref="CrewLocation.Eva"/> when suited, otherwise in a cabin.</summary>
    public CrewLocation Location => CurrentLocation.IsSuited ? CrewLocation.Eva : CrewLocation.Cabin;

    /// <summary>The task this member is performing, if any.</summary>
    public TaskId? Activity { get; internal set; }

    public bool IsBusy => Activity is not null;

    /// <summary>Angular rate from the latest health step; drives the rate impairment of tasks.</summary>
    public double AngularRateDps { get; internal set; }

    /// <summary>Total damage from every cause since launch, including damage later recovered.</summary>
    public double MissionDamage
    {
        get
        {
            var sum = 0.0;
            for (var i = 0; i < _damageByCause.Length; i++) sum += _damageByCause[i];
            return sum;
        }
    }

    /// <summary>Index of <see cref="CurrentLocation"/> in the roster's <see cref="CrewLocations"/>.</summary>
    internal int LocationIndex { get; set; }

    internal int HomeIndex { get; set; }

    /// <summary>The home was moved while a task was running; the move then wins over the task's <c>movesTo</c>.</summary>
    internal bool MovedDuringTask { get; set; }

    public double DamageFrom(HealthCause cause) => _damageByCause[(int)cause];

    internal double[] DamageByCause => _damageByCause;

    /// <summary>How long each cause has been continuously harmful, for sustained-exposure curves.</summary>
    internal double[] HarmfulForS => _harmfulForS;

    internal void WriteHash(StateHashWriter writer)
    {
        writer.Write(Id.Value);
        writer.Write(Health);
        writer.Write(WorstHealth);
        writer.Write((long)Band);
        // One injective value for both indices and the move flag; with the home at the default and no move it is the location
        // index alone, which for mission 1's sorted (cabin, eva) equals the former CrewLocation value, so existing hashes are unchanged.
        long count = _locations.Count;
        writer.Write(LocationIndex + count * (HomeIndex - _locations.DefaultIndex) + (MovedDuringTask ? 2 * count * count : 0));
        writer.Write(Activity?.Value ?? "");
        writer.Write(AngularRateDps);
        for (var i = 0; i < _damageByCause.Length; i++)
        {
            writer.Write(_damageByCause[i]);
            writer.Write(_harmfulForS[i]);
        }
    }
}
