using Usa.Simulation.Data;

namespace Usa.Game.Flow.Data;

/// <summary>
/// Demo-build cuts read only by the front end (<c>config/demo.json</c>, docs/dev/DEMO_CUTS.md). The simulation never sees
/// them, so scenarios and tests still fly every difficulty. Without the file the full game is offered.
/// </summary>
public sealed record DemoScope(string Schema, IReadOnlyList<string> HiddenDifficulties, bool RetryNewVariant) : IVersionedDefinition
{
    public const string SchemaTag = "demo/1";
    public const string FilePath = "config/demo.json";

    public static DemoScope Full { get; } = new(SchemaTag, [], true);

    /// <exception cref="DefinitionException">The file exists but is malformed.</exception>
    public static DemoScope Load(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Exists(FilePath) ? new DefinitionLoader(source).Load<DemoScope>(FilePath, SchemaTag) : Full;
    }

    public bool Offers(string difficultyId) => !HiddenDifficulties.Contains(difficultyId, StringComparer.Ordinal);
}
