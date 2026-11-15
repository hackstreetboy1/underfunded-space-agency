using Usa.Game.Content;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Workspace;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>What the Assign step sent: the design registration and, when it was accepted, the install order.</summary>
/// <param name="Install">Null when the registration was refused (nothing was installed).</param>
public sealed record RepairSubmission(RepairAssignment Assignment, ValidationResult Registration, ValidationResult? Install);

/// <summary>
/// Creates workspace models over the running session (GDD 8). Each model reads the session's spares through a
/// <see cref="SessionInventoryMirror"/> refreshed on every <c>InventoryChanged</c>; Assign submits
/// <c>register_repair_design</c> (workspace source, so the command log replays the design) and, once that is accepted,
/// <c>install_repair</c> for the chosen crew member. The Assign step is the player's confirmation, so both go confirmed.
/// </summary>
public sealed class RepairWorkspaceFactory : IDisposable
{
    public const string InstallAction = "install_repair";

    private readonly MissionSession _session;
    private readonly RuntimeCommands _commands;
    private readonly ITextSource _text;
    private readonly WorkspaceOptions? _options;
    private readonly SessionInventoryMirror _inventory;
    private readonly RepairAssignmentEstimator _estimator;
    private readonly RepairReservationTracker _reservations;
    private readonly TaskRateRequirements _requirements;
    private readonly List<WorkspaceModel> _models = [];
    private bool _disposed;

    public RepairWorkspaceFactory(MissionSession session, RuntimeCommands commands, ITextSource text, WorkspaceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(text);
        _session = session;
        _commands = commands;
        _text = text;
        _options = options;
        _inventory = new SessionInventoryMirror(session);
        _estimator = new RepairAssignmentEstimator(session);
        _reservations = new RepairReservationTracker(session.Workspace);
        _requirements = new TaskRateRequirements(session);
    }

    /// <summary>An Assign was forwarded to the command pipeline; show refusals from here.</summary>
    public event Action<RepairSubmission>? Submitted;

    public Inventory Inventory => _inventory.Inventory;

    /// <summary>What each goal's ready registration holds, so a reopened goal counts its own parts as available.</summary>
    public IRepairReservations Reservations => _reservations;

    /// <summary>A new model; it stays wired to the session until <see cref="Release"/> or <see cref="Dispose"/>.</summary>
    public WorkspaceModel Create()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var model = new WorkspaceModel(_session.Workspace.Parts, _session.Workspace.Goals, _inventory.Inventory, _session.State, _estimator, _text, _options, _reservations, _requirements);
        model.Assigned += OnAssigned;
        _models.Add(model);
        return model;
    }

    /// <summary>The workspace screen closed: stop forwarding its assignments and inventory changes.</summary>
    public bool Release(WorkspaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!_models.Remove(model)) return false;
        model.Assigned -= OnAssigned;
        return true;
    }

    public void OnSimEvent(ISimEvent simEvent)
    {
        if (simEvent is not InventoryChanged || _disposed) return;
        _inventory.Sync(_session.Now);
        foreach (var model in _models.ToArray()) model.RefreshInventory();
    }

    /// <summary>Registers <paramref name="assignment"/>'s design, then orders its installation.</summary>
    public RepairSubmission Submit(RepairAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var registerArgs = new Dictionary<string, CommandArg>(StringComparer.Ordinal)
        {
            [RepairDesignArgs.Assembly] = CommandArg.Of(assignment.AssemblyId),
            [RepairDesignArgs.Design] = CommandArg.Of(RepairDesignCodec.Serialize(assignment.Design)),
        };
        var registration = _commands.Submit(new CommandRequest(
            _commands.NextId(), CommandSource.Workspace, new ActionId(MissionSession.RegisterDesignAction), null, registerArgs, Confirmed: true));

        ValidationResult? install = null;
        if (registration.IsValid)
        {
            var installArgs = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { ["assembly"] = CommandArg.Of(assignment.AssemblyId) };
            install = _commands.Submit(new CommandRequest(
                _commands.NextId(), CommandSource.Workspace, new ActionId(InstallAction), assignment.Assignee, installArgs, Confirmed: true));
        }

        var submission = new RepairSubmission(assignment, registration, install);
        _reservations.Record(submission);
        Submitted?.Invoke(submission);
        return submission;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var model in _models) model.Assigned -= OnAssigned;
        _models.Clear();
    }

    private void OnAssigned(object? sender, RepairAssignment assignment) => Submit(assignment);
}
