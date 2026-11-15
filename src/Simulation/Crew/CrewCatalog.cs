using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew;

/// <summary>Validated crew definitions (sorted by id) and performance tuning from <c>crew/crew.json</c>.</summary>
public sealed class CrewCatalog
{
    public const string DefaultPath = "crew/crew.json";
    public const string SchemaTag = "crew/1";

    private readonly CrewMemberDefinition[] _members;

    private CrewCatalog(CrewMemberDefinition[] members, CrewPerformance performance)
    {
        _members = members;
        Performance = performance;
    }

    public IReadOnlyList<CrewMemberDefinition> Members => _members;

    public CrewPerformance Performance { get; }

    public bool TryGet(CrewId id, [MaybeNullWhen(false)] out CrewMemberDefinition member)
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

    public static CrewCatalog Load(DefinitionLoader loader, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<CrewFileDefinition>(path, SchemaTag), path);
    }

    /// <exception cref="DefinitionException">Every problem in the file, reported together.</exception>
    public static CrewCatalog Create(CrewFileDefinition file, string fileLabel)
    {
        ArgumentNullException.ThrowIfNull(file);
        var errors = new DefinitionErrors();
        if (file.Members.Count == 0) errors.Add(fileLabel, "$.members", "At least one crew member is required.");

        var seen = new HashSet<CrewId>();
        for (var i = 0; i < file.Members.Count; i++)
        {
            var member = file.Members[i];
            var path = string.Create(CultureInfo.InvariantCulture, $"$.members[{i}]");
            if (!seen.Add(member.Id)) errors.Add(fileLabel, path + ".id", $"Duplicate crew id '{member.Id}'.");
            if (IsReservedOrUnsafe(member.Id.Value))
                errors.Add(fileLabel, path + ".id", $"Crew id '{member.Id}' clashes with a crew state path or contains '.' or '/'.");
            if (string.IsNullOrWhiteSpace(member.Name)) errors.Add(fileLabel, path + ".name", "Name is empty.");
            if (string.IsNullOrWhiteSpace(member.Role)) errors.Add(fileLabel, path + ".role", "Role is empty.");
            if (!(member.Experience is >= 0 and <= 1)) errors.Add(fileLabel, path + ".experience", "Experience must be in [0, 1].");
            foreach (var domain in Enum.GetValues<SkillDomain>())
            {
                var domainName = JsonNamingPolicy.SnakeCaseLower.ConvertName(domain.ToString());
                if (!member.Skills.TryGetValue(domain, out var level))
                    errors.Add(fileLabel, path + ".skills", $"Skill '{domainName}' is missing.");
                else if (level is < 1 or > 5)
                    errors.Add(fileLabel, $"{path}.skills.{domainName}", "Skill levels are 1-5.");
            }
        }

        var performance = CrewPerformance.Create(file.Performance, fileLabel, errors);
        errors.ThrowIfAny();

        var members = file.Members.ToArray();
        Array.Sort(members, (a, b) => a.Id.CompareTo(b.Id));
        return new CrewCatalog(members, performance!);
    }

    private static bool IsReservedOrUnsafe(string id) =>
        id is "count" or "alive_count" or "task" or "ration"
        || id.Contains('.', StringComparison.Ordinal)
        || id.Contains('/', StringComparison.Ordinal);
}
