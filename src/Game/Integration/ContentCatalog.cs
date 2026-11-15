using Usa.Game.Content;
using Usa.Simulation.Data;

namespace Usa.Game.Integration;

/// <summary>Loads each mission's <see cref="ContentLibrary"/> once, on first use, from the game's data source.</summary>
public sealed class ContentCatalog
{
    private readonly IDataSource _data;
    private readonly Dictionary<string, ContentLibrary> _loaded = new(StringComparer.Ordinal);

    public ContentCatalog(IDataSource data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    /// <exception cref="DefinitionException">Every problem in the mission's content files.</exception>
    public ContentLibrary Get(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        if (_loaded.TryGetValue(missionId, out var library)) return library;
        library = ContentLibrary.Load(_data, missionId);
        _loaded[missionId] = library;
        return library;
    }
}
