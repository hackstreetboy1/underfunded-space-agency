using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Adapters;

/// <summary>Display names and roles of the mission's crew (<c>crew/crew.json</c>).</summary>
public sealed class CrewNames
{
    private readonly Dictionary<CrewId, CrewMemberDefinition> _members = [];

    public CrewNames(CrewCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var member in catalog.Members) _members[member.Id] = member;
    }

    public string FullName(CrewId id) => _members.TryGetValue(id, out var member) ? member.Name : id.Value;

    /// <summary>Family name as the log and transcript use it, e.g. <c>Okafor</c>.</summary>
    public string ShortName(CrewId id)
    {
        var name = FullName(id);
        var space = name.LastIndexOf(' ');
        return space >= 0 ? name[(space + 1)..] : name;
    }

    public string Role(CrewId id) => _members.TryGetValue(id, out var member) ? member.Role : string.Empty;

    public bool TryGet(CrewId id, out CrewMemberDefinition member) => _members.TryGetValue(id, out member!);
}
