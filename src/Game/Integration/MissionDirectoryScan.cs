using Godot;
using Usa.Game.Services.Data;
using Usa.Simulation.Session;

namespace Usa.Game.Integration;

/// <summary>Finds playable missions: directories under <c>missions/</c> of the data root that hold a <c>mission.json</c>.</summary>
public static class MissionDirectoryScan
{
    public static IReadOnlyList<string> List(GodotDataSource data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var root = $"{data.Root}/missions";
        if (!DirAccess.DirExistsAbsolute(root)) return [];

        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in DirAccess.GetDirectoriesAt(root))
        {
            if (data.Exists($"{MissionData.MissionDirectory(name)}/mission.json")) ids.Add(name);
        }
        return [.. ids];
    }
}
