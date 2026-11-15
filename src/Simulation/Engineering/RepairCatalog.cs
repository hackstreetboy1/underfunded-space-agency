using Usa.Simulation.Data;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Engineering;

/// <summary>The repair goals of a mission, sorted by id.</summary>
public sealed class RepairCatalog
{
    public const string SchemaTag = "repairs/1";

    private readonly Dictionary<string, RepairGoal> _goals;

    private RepairCatalog(RepairGoal[] goals)
    {
        Goals = goals;
        _goals = goals.ToDictionary(g => g.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<RepairGoal> Goals { get; }

    public static string PathFor(string missionId) => $"missions/{missionId}/repairs.json";

    public static RepairCatalog Load(DefinitionLoader loader, string relativePath, PartCatalog parts, IExpressionCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<RepairsFile>(relativePath, SchemaTag), relativePath, parts, compiler);
    }

    /// <summary>
    /// Validates and compiles the goals. Component ids and expression paths belong to other areas, so the
    /// composition root should also check them against the live state query (see <see cref="StatePathsUsed"/>).
    /// </summary>
    /// <exception cref="DefinitionException">Every problem in the file.</exception>
    public static RepairCatalog Create(RepairsFile file, string fileLabel, PartCatalog parts, IExpressionCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(compiler);
        var errors = new DefinitionErrors();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var goals = new List<RepairGoal>(file.Goals.Count);

        for (var g = 0; g < file.Goals.Count; g++)
        {
            var goal = file.Goals[g];
            var path = $"$.goals[{g}]";
            if (!PartCatalog.IsSnakeCase(goal.Id)) errors.Add(fileLabel, path + ".id", $"'{goal.Id}' is not a snake_case id.");
            else if (!ids.Add(goal.Id)) errors.Add(fileLabel, path + ".id", $"Duplicate repair goal '{goal.Id}'.");
            if (string.IsNullOrWhiteSpace(goal.Name)) errors.Add(fileLabel, path + ".name", "Name is required.");
            ValidatePort(errors, fileLabel, path + ".source", goal.Source.Label, goal.Source.Connector, parts);
            if (!(goal.LoadCurrentA > 0)) errors.Add(fileLabel, path + ".loadCurrentA", "Must be > 0.");
            if (!(goal.WireRatingA >= goal.LoadCurrentA)) errors.Add(fileLabel, path + ".wireRatingA", "Must be >= the load current.");
            if (!(goal.MinLengthM >= 0)) errors.Add(fileLabel, path + ".minLengthM", "Must be >= 0.");
            ValidateTask(errors, fileLabel, path + ".task", goal.Task);

            if (goal.SinkCandidates.Count == 0) errors.Add(fileLabel, path + ".sinkCandidates", "At least one sink is required.");
            var sinks = new List<RepairSink>(goal.SinkCandidates.Count);
            for (var s = 0; s < goal.SinkCandidates.Count; s++)
            {
                var sink = goal.SinkCandidates[s];
                var sinkPath = $"{path}.sinkCandidates[{s}]";
                ValidatePort(errors, fileLabel, sinkPath, sink.Label, sink.Connector, parts);
                try
                {
                    sinks.Add(new RepairSink(sink, compiler.Compile(sink.When)));
                }
                catch (ExpressionException ex)
                {
                    errors.Add(fileLabel, sinkPath + ".when", ex.Message);
                }
            }
            goals.Add(new RepairGoal(goal, sinks));
        }
        errors.ThrowIfAny();
        return new RepairCatalog([.. goals.OrderBy(g => g.Id, StringComparer.Ordinal)]);
    }

    public bool TryGet(string goalId, out RepairGoal goal) => _goals.TryGetValue(goalId, out goal!);

    public RepairGoal Get(string goalId) =>
        _goals.TryGetValue(goalId, out var goal) ? goal : throw new KeyNotFoundException($"Unknown repair goal '{goalId}'.");

    /// <summary>Every state path the goals read: sink conditions plus each referenced component's condition.</summary>
    public IReadOnlyList<string> StatePathsUsed()
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var goal in Goals)
        {
            paths.Add($"{goal.Source.Component}.condition");
            foreach (var sink in goal.SinkCandidates)
            {
                paths.Add($"{sink.Definition.Component}.condition");
                paths.Add($"{sink.Definition.Repairs}.condition");
                foreach (var path in sink.When.Paths) paths.Add(path);
            }
        }
        return [.. paths];
    }

    private static void ValidatePort(DefinitionErrors errors, string file, string path, string label, string connector, PartCatalog parts)
    {
        if (string.IsNullOrWhiteSpace(label)) errors.Add(file, path + ".label", "Label is required.");
        if (!parts.IsConnectorType(connector)) errors.Add(file, path + ".connector", $"Unknown connector type '{connector}'.");
    }

    private static void ValidateTask(DefinitionErrors errors, string file, string path, RepairTaskParameters task)
    {
        if (!PartCatalog.IsSnakeCase(task.Skill)) errors.Add(file, path + ".skill", $"'{task.Skill}' is not a snake_case skill id.");
        if (!(task.BaseDurationS >= 0)) errors.Add(file, path + ".baseDurationS", "Must be >= 0.");
        if (!(task.DurationPerPartS >= 0)) errors.Add(file, path + ".durationPerPartS", "Must be >= 0.");
        if (task.MaxComplexity < 1) errors.Add(file, path + ".maxComplexity", "Must be >= 1.");
        if (task.BaseComplexity < 1 || task.BaseComplexity > task.MaxComplexity)
            errors.Add(file, path + ".baseComplexity", "Must be between 1 and maxComplexity.");
        if (task.PartsPerComplexityStep < 1) errors.Add(file, path + ".partsPerComplexityStep", "Must be >= 1.");
    }
}
