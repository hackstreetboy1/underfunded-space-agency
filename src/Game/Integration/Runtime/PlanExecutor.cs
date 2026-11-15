using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Map.Planning;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>How a confirmed plan reaches the simulation.</summary>
public enum BurnOrderKind
{
    /// <summary>A crew task (<c>oams_burn</c>, <c>retrofire</c>): the crew fire the burn when the task completes.</summary>
    CrewOrder,

    /// <summary>A ground command (<c>lodestar_burn</c>): the engine fires when the command arrives.</summary>
    RemoteCommand,
}

/// <summary>
/// The order a planned burn becomes (UI 4.1: Execute "creates crew order or remote command as the engine requires"). The
/// session has no burn clock of its own: a crew burn fires when its task completes and a remote burn when its command
/// arrives, so the order is given at <see cref="OrderAt"/> = planned start − the task's duration ("retrofire on the mark").
/// </summary>
/// <param name="EngineId">The planner option (a <c>session.json</c> <c>burns</c> id, or <see cref="PlannerEngineOption.RetroSequence"/>).</param>
/// <param name="Args">The plan as the action's parameters: <c>direction_deg</c> (inertial, at the planned start) and <c>duration_s</c>.</param>
public sealed record BurnOrder(
    string EngineId,
    CatalogAction Action,
    BurnOrderKind Kind,
    IReadOnlyDictionary<string, CommandArg> Args,
    MissionTime StartAt,
    MissionTime OrderAt)
{
    /// <summary>High-impact and irreversible orders need the player's confirmation before they are given or held.</summary>
    public bool NeedsConfirmation => Action.BaseImpact != ActionImpact.Low;
}

public enum PlanExecutionStatus
{
    /// <summary>The order was sent now; <see cref="PlanExecution.Result"/> is the session's answer.</summary>
    Sent,

    /// <summary>Confirmed and held: the runtime gives the order at <see cref="BurnOrder.OrderAt"/>.</summary>
    Held,

    /// <summary>Ask the player (high-impact), then execute again with <c>confirmed: true</c>.</summary>
    NeedsConfirmation,

    /// <summary>No action fires this engine, or a parameter cannot be filled (see <see cref="PlanExecution.Reason"/>).</summary>
    Unavailable,
}

public sealed record PlanExecution(PlanExecutionStatus Status, BurnOrder? Order, ValidationResult? Result = null, string? Reason = null);

/// <summary>
/// Turns the burn planner's Execute into the order the mission's data defines for the engine, through the runtime's single
/// command path (<see cref="RuntimeCommands"/>): the action whose effects (or whose task's success effects) write
/// <c>session.burn.&lt;engine&gt;.*</c>, or, for the retro sequence, the action that affects the retro rockets. The plan's
/// direction and duration become its parameters; nothing writes simulation state directly. A confirmed order whose mark lies
/// ahead is held and given by <see cref="Poll"/> at the mark; the session still validates it then (crew busy, phase, docked).
/// </summary>
public sealed class PlanExecutor
{
    private const string BurnPathPrefix = "session.burn.";
    private const string DirectionParameter = "direction_deg";
    private const string DurationParameter = "duration_s";

    private readonly MissionSession _session;
    private readonly RuntimeCommands _commands;
    private readonly FlightTrack _track;
    private readonly Dictionary<string, CatalogAction> _actions = new(StringComparer.Ordinal);

    public PlanExecutor(MissionSession session, RuntimeCommands commands, FlightTrack track)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(track);
        _session = session;
        _commands = commands;
        _track = track;

        var rockets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var vehicle in session.Data.Vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                if (component.RetroRocket is not null) rockets.Add(component.Id.Value);
            }
        }
        foreach (var action in session.Data.Actions.Actions)
        {
            if (action.Kind is not (ActionKind.CrewTask or ActionKind.Remote)) continue;
            foreach (var target in EffectTargets(session, action))
            {
                if (BurnId(target) is { } burn) _actions.TryAdd(burn, action);
            }
            if (action.Definition.Affects is { } affects && affects.Any(rockets.Contains)) _actions.TryAdd(PlannerEngineOption.RetroSequence, action);
        }
    }

    /// <summary>The confirmed order waiting for its mark; null when none.</summary>
    public BurnOrder? Pending { get; private set; }

    /// <summary>The order <paramref name="plan"/> on <paramref name="engineId"/> becomes; null when the data has none.</summary>
    public BurnOrder? Prepare(ManeuverPlan plan, string engineId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(engineId);
        if (!_actions.TryGetValue(engineId, out var action)) return null;

        var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal);
        foreach (var parameter in action.Parameters)
        {
            switch (parameter.Name)
            {
                case DirectionParameter when DirectionDeg(plan) is { } degrees:
                    args[parameter.Name] = CommandArg.Of(degrees);
                    break;
                case DurationParameter:
                    args[parameter.Name] = CommandArg.Of(plan.EffectiveDurationS);
                    break;
                default:
                    return null;
            }
        }

        var kind = action.Kind == ActionKind.CrewTask ? BurnOrderKind.CrewOrder : BurnOrderKind.RemoteCommand;
        var leadS = kind == BurnOrderKind.CrewOrder && action.Definition.TaskId is { } task && _session.Data.Tasks.TryGet(task, out var definition)
            ? definition.BaseDurationS
            : 0;
        var orderAt = new MissionTime(Math.Max(0, plan.StartTime.Milliseconds - (long)Math.Round(leadS * 1000)));
        return new BurnOrder(engineId, action, kind, args, plan.StartTime, orderAt);
    }

    /// <summary>
    /// Execute: sends the order now when its mark has come, else holds it (high-impact orders only once confirmed). A new
    /// execution replaces a held order.
    /// </summary>
    /// <param name="confirmed">The player accepted the high-impact confirmation for this plan.</param>
    public PlanExecution Execute(ManeuverPlan plan, string engineId, bool confirmed)
    {
        if (Prepare(plan, engineId) is not { } order)
            return new PlanExecution(PlanExecutionStatus.Unavailable, null, Reason: $"no order fires '{engineId}' with this plan");
        if (order.NeedsConfirmation && !confirmed) return new PlanExecution(PlanExecutionStatus.NeedsConfirmation, order);

        Pending = null;
        if (order.OrderAt > _session.Now)
        {
            Pending = order;
            return new PlanExecution(PlanExecutionStatus.Held, order);
        }
        return new PlanExecution(PlanExecutionStatus.Sent, order, Send(order, confirmed));
    }

    /// <summary>Drops the held order (Clear, or a new plan).</summary>
    public void Cancel() => Pending = null;

    /// <summary>Gives the held order once its mark has come; returns the session's answer, or null when nothing was sent.</summary>
    public ValidationResult? Poll()
    {
        if (Pending is not { } order || _session.Now < order.OrderAt) return null;
        Pending = null;
        return Send(order, confirmed: true);
    }

    private ValidationResult Send(BurnOrder order, bool confirmed)
    {
        var source = order.Kind == BurnOrderKind.CrewOrder ? CommandSource.QuickCommand : CommandSource.RemoteControl;
        return _commands.Submit(new CommandRequest(_commands.NextId(), source, order.Action.Id, null, order.Args, confirmed));
    }

    /// <summary>The thrust's inertial angle at the planned start, on the ground's estimated orbit (the session's convention, 0–360°).</summary>
    private double? DirectionDeg(ManeuverPlan plan)
    {
        if (_track.EstimatedConic is not { } conic) return null;
        var state = conic.StateAt(plan.StartTime);
        var radians = BurnFrames.InertialAngleRad(plan.Direction, plan.CustomAngleDeg * Angles.DegToRad, state);
        var degrees = Angles.WrapPositive(radians) * Angles.RadToDeg;
        return degrees >= 360 ? 0 : degrees;
    }

    private static IEnumerable<string> EffectTargets(MissionSession session, CatalogAction action)
    {
        if (action.Definition.Effects is { } effects)
        {
            foreach (var effect in effects) yield return effect.Target;
        }
        if (action.Definition.TaskId is { } task && session.Data.Tasks.TryGet(task, out var definition))
        {
            foreach (var effect in definition.OnSuccess) yield return effect.Target;
        }
    }

    /// <summary><c>session.burn.&lt;id&gt;.&lt;field&gt;</c> → id.</summary>
    private static string? BurnId(string target)
    {
        if (!target.StartsWith(BurnPathPrefix, StringComparison.Ordinal)) return null;
        var rest = target.AsSpan(BurnPathPrefix.Length);
        var dot = rest.IndexOf('.');
        return dot > 0 ? rest[..dot].ToString() : null;
    }
}
