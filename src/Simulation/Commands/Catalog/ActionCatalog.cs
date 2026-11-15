using System.Diagnostics.CodeAnalysis;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>Every action the player or the AI can request, compiled once at load and listed in id order.</summary>
public sealed class ActionCatalog
{
    private readonly Dictionary<ActionId, CatalogAction> _byId;
    private readonly CatalogAction[] _actions;

    private ActionCatalog(CatalogAction[] actionsInIdOrder)
    {
        _actions = actionsInIdOrder;
        _byId = actionsInIdOrder.ToDictionary(action => action.Id);
    }

    public IReadOnlyList<CatalogAction> Actions => _actions;

    /// <exception cref="DefinitionException">Every problem in the file, reported together.</exception>
    public static ActionCatalog Load(
        DefinitionLoader loader,
        IExpressionCompiler compiler,
        ActionCatalogChecks? checks = null,
        string path = ActionsFile.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<ActionsFile>(path, ActionsFile.SchemaTag), compiler, checks, path);
    }

    /// <exception cref="DefinitionException">Every problem in the definitions, reported together.</exception>
    public static ActionCatalog Create(
        ActionsFile file,
        IExpressionCompiler compiler,
        ActionCatalogChecks? checks = null,
        string fileLabel = ActionsFile.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(compiler);
        var errors = new DefinitionErrors();
        if (file.Schema != ActionsFile.SchemaTag)
            errors.Add(fileLabel, "$.schema", $"Expected schema '{ActionsFile.SchemaTag}' but found '{file.Schema}'.");

        var actionCompiler = new ActionCompiler(compiler, checks ?? new ActionCatalogChecks(), fileLabel, errors);
        var seen = new HashSet<ActionId>();
        var actions = new List<CatalogAction>(file.Actions.Count);
        for (var i = 0; i < file.Actions.Count; i++)
        {
            var path = $"$.actions[{i}]";
            var definition = file.Actions[i];
            if (!seen.Add(definition.Id)) errors.Add(fileLabel, path + ".id", $"Duplicate action id '{definition.Id}'.");
            if (actionCompiler.Compile(definition, path) is { } action) actions.Add(action);
        }

        errors.ThrowIfAny();
        actions.Sort((a, b) => a.Id.CompareTo(b.Id));
        return new ActionCatalog([.. actions]);
    }

    public bool TryGet(ActionId id, [NotNullWhen(true)] out CatalogAction? action) => _byId.TryGetValue(id, out action);

    /// <summary>The phase allowlist, in id order.</summary>
    public IReadOnlyList<CatalogAction> AllowedIn(string phase)
    {
        var allowed = new List<CatalogAction>();
        foreach (var action in _actions)
        {
            if (action.IsAllowedIn(phase)) allowed.Add(action);
        }
        return allowed;
    }
}
