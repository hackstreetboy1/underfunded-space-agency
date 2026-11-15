using System.Diagnostics.CodeAnalysis;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew;

/// <summary>
/// The crew's mutable state, served under <c>crew.</c>:
/// <c>crew.count</c>, <c>crew.alive_count</c>, <c>crew.task.active</c>, <c>crew.task.id</c>, <c>crew.task.assignee</c>,
/// <c>crew.ration.food_pct</c>, <c>crew.ration.water_pct</c>, and per member <c>crew.&lt;id&gt;.</c>
/// <c>health</c>, <c>alive</c>, <c>band</c>, <c>busy</c>, <c>location</c> (a <see cref="CrewLocations"/> id), <c>home_location</c>, <c>activity</c>,
/// <c>launch_health</c>, <c>worst_health</c>, <c>mission_damage</c>, <c>complication</c>.
/// </summary>
public sealed class CrewRoster : IStateProvider, IHashContributor
{
    public const double FullRationPct = 100.0;

    private readonly CrewMember[] _members;
    private readonly double _complicationThreshold;

    /// <summary>A roster with mission 1's places (<see cref="CrewLocations.Basic"/>).</summary>
    public CrewRoster(CrewCatalog catalog, HealthConfig health)
        : this(catalog, health, CrewLocations.Basic())
    {
    }

    public CrewRoster(CrewCatalog catalog, HealthConfig health, CrewLocations locations)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(health);
        Locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _complicationThreshold = health.ComplicationThreshold;
        _members = new CrewMember[catalog.Members.Count];
        for (var i = 0; i < _members.Length; i++)
        {
            _members[i] = new CrewMember(catalog.Members[i], health.LaunchHealth, health.BandFor(health.LaunchHealth), locations);
        }
    }

    public CrewLocations Locations { get; }

    public string Prefix => StatePaths.Crew;

    /// <summary>Members in id order.</summary>
    public IReadOnlyList<CrewMember> Members => _members;

    public int AliveCount
    {
        get
        {
            var count = 0;
            foreach (var member in _members)
            {
                if (member.IsAlive) count++;
            }
            return count;
        }
    }

    /// <summary>The member holding the task slot, if any.</summary>
    public CrewMember? ActiveAssignee
    {
        get
        {
            foreach (var member in _members)
            {
                if (member.IsBusy) return member;
            }
            return null;
        }
    }

    public double FoodRationPct { get; internal set; } = FullRationPct;

    public double WaterRationPct { get; internal set; } = FullRationPct;

    internal CrewMember[] MemberArray => _members;

    public CrewMember this[CrewId id] =>
        TryFind(id, out var member) ? member : throw new KeyNotFoundException($"Unknown crew member '{id}'.");

    public bool TryFind(CrewId id, [MaybeNullWhen(false)] out CrewMember member)
    {
        foreach (var candidate in _members)
        {
            if (candidate.Id == id)
            {
                member = candidate;
                return true;
            }
        }
        member = null;
        return false;
    }

    public bool HasComplication(CrewMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.MissionDamage > _complicationThreshold;
    }

    /// <summary>
    /// The member a task addressed to "crew" goes to: alive, free and where the task requires, highest mean skill
    /// in the task's domains, then highest health, then lowest id.
    /// </summary>
    public CrewMember? BestAvailable(TaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(task);
        CrewMember? best = null;
        var bestSkill = double.NegativeInfinity;
        foreach (var member in _members)
        {
            if (!member.IsAlive || member.IsBusy || !IsWhereRequired(member, task)) continue;
            var skill = CrewPerformance.SkillLevel(task, member.Definition);
            if (best is null || skill > bestSkill || (skill == bestSkill && member.Health > best.Health))
            {
                best = member;
                bestSkill = skill;
            }
        }
        return best;
    }

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        if (path is null || !path.StartsWith(StatePaths.Crew, StringComparison.Ordinal)) return false;
        var rest = path.AsSpan(StatePaths.Crew.Length);
        switch (rest)
        {
            case "count":
                value = _members.Length;
                return true;
            case "alive_count":
                value = AliveCount;
                return true;
            case "task.active":
                value = ActiveAssignee is not null;
                return true;
            case "task.id":
                value = ActiveAssignee?.Activity?.Value ?? "";
                return true;
            case "task.assignee":
                value = ActiveAssignee?.Id.Value ?? "";
                return true;
            case "ration.food_pct":
                value = FoodRationPct;
                return true;
            case "ration.water_pct":
                value = WaterRationPct;
                return true;
        }

        var dot = rest.IndexOf('.');
        if (dot <= 0) return false;
        var member = Find(rest[..dot]);
        if (member is null) return false;
        switch (rest[(dot + 1)..])
        {
            case "health":
                value = member.Health;
                return true;
            case "alive":
                value = member.IsAlive;
                return true;
            case "band":
                value = BandName(member.Band);
                return true;
            case "busy":
                value = member.IsBusy;
                return true;
            case "location":
                value = member.LocationId;
                return true;
            case "home_location":
                value = member.HomeLocation.Id;
                return true;
            case "activity":
                value = member.Activity?.Value ?? "";
                return true;
            case "launch_health":
                value = member.LaunchHealth;
                return true;
            case "worst_health":
                value = member.WorstHealth;
                return true;
            case "mission_damage":
                value = member.MissionDamage;
                return true;
            case "complication":
                value = HasComplication(member);
                return true;
            default:
                return false;
        }
    }

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var member in _members) member.WriteHash(writer);
        writer.Write(FoodRationPct);
        writer.Write(WaterRationPct);
    }

    public static string BandName(HealthBand band) => band switch
    {
        HealthBand.Stable => "stable",
        HealthBand.Stressed => "stressed",
        HealthBand.Impaired => "impaired",
        HealthBand.Critical => "critical",
        HealthBand.Deceased => "deceased",
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };

    /// <summary>True when the task names no required location or the member's home location is one of them.</summary>
    public static bool IsWhereRequired(CrewMember member, TaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(task);
        if (task.RequiredLocations.Count == 0) return true;
        var home = member.HomeLocation.Id;
        foreach (var required in task.RequiredLocations)
        {
            if (string.Equals(required, home, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Moves a living member's home location (e.g. everyone back to the ferry on undocking). A busy member stays at
    /// the task's location until it ends, then returns to the new home: the move wins over the task's <c>movesTo</c>, and the
    /// task runner stops a task whose required location the new home no longer satisfies.
    /// </summary>
    /// <returns>False for an unknown member or location, a suited location (no one lives on EVA), or a dead member.</returns>
    public bool TryMove(CrewId crew, string locationId)
    {
        var index = Locations.IndexOf(locationId);
        if (index < 0 || Locations[index].IsSuited || !TryFind(crew, out var member) || !member.IsAlive) return false;
        member.HomeIndex = index;
        if (member.IsBusy) member.MovedDuringTask = true;
        else member.LocationIndex = index;
        return true;
    }

    /// <exception cref="InvalidOperationException">The task names a location this mission does not have (a data error), checked before anything changes.</exception>
    internal void BeginActivity(CrewMember member, TaskDefinition task)
    {
        var index = task.LocationId == CrewLocations.CurrentId ? member.HomeIndex : Locations.IndexOf(task.LocationId);
        if (index < 0) throw new InvalidOperationException($"Task '{task.Id}' takes place at unknown crew location '{task.LocationId}'.");
        if (task.MovesTo is { } movesTo && !Locations.Contains(movesTo))
            throw new InvalidOperationException($"Task '{task.Id}' moves its assignee to unknown crew location '{movesTo}'.");
        member.Activity = task.Id;
        member.LocationIndex = index;
    }

    /// <param name="movesTo">The location the member stays at from now on; null returns them home.</param>
    internal void EndActivity(CrewMember member, string? movesTo = null)
    {
        if (movesTo is not null && !member.MovedDuringTask)
        {
            var index = Locations.IndexOf(movesTo);
            if (index < 0) throw new InvalidOperationException($"Unknown crew location '{movesTo}'.");
            member.HomeIndex = index;
        }
        member.Activity = null;
        member.MovedDuringTask = false;
        member.LocationIndex = member.HomeIndex;
    }

    private CrewMember? Find(ReadOnlySpan<char> id)
    {
        foreach (var member in _members)
        {
            if (id.SequenceEqual(member.Id.Value)) return member;
        }
        return null;
    }
}
