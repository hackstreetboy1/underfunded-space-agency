using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// Serves and applies the <c>session.</c> paths of SESSION_EFFECTS_NEEDED.md. Every target is also readable. Burn paths exist
/// for the engines the mission's <c>session.json</c> declares (<see cref="BurnController"/>):
/// <list type="table">
/// <item><term>session.burn.&lt;translation&gt;.direction_deg / duration_s</term><description>R W (set, number): stage the next translation burn.</description></item>
/// <item><term>session.burn.&lt;translation&gt;.start</term><description>R (true while burning) W (set true starts the staged burn).</description></item>
/// <item><term>session.burn.&lt;docked&gt;.duration_s</term><description>R (remaining seconds) W (set N fires the engine for N s, docked only).</description></item>
/// <item><term>session.burn.&lt;docked&gt;.docked_dv_mps</term><description>R (number): prograde Δv the engine added while docked (mission events compare it).</description></item>
/// <item><term>session.repair.install / partial</term><description>R W (set, text assembly id): see <see cref="RepairWorkspace"/>.</description></item>
/// <item><term>session.repair.&lt;assemblyId&gt;.ready</term><description>R (bool) for any id.</description></item>
/// <item><term>session.entry_checklist.armed</term><description>R W (set, bool): see <see cref="ChuteSequencer"/>.</description></item>
/// </list>
/// </summary>
internal sealed class SessionPaths(BurnController burns, RepairWorkspace repairs, ChuteSequencer chutes, Func<MissionTime> now)
    : IStateProvider, IEffectHandler
{
    public const string PathPrefix = "session.";
    public const string BurnPrefix = "session.burn.";
    public const string RepairInstall = "session.repair.install";
    public const string RepairPartial = "session.repair.partial";
    public const string EntryChecklistArmed = "session.entry_checklist.armed";
    public const string DirectionField = "direction_deg";
    public const string DurationField = "duration_s";
    public const string StartField = "start";
    public const string DockedDvField = "docked_dv_mps";
    private const string RepairPrefix = "session.repair.";
    private const string ReadySuffix = ".ready";

    private readonly Dictionary<string, BurnPathKey> _burnPaths = BurnPaths(burns);

    public string Prefix => PathPrefix;

    /// <summary><c>session.burn.&lt;engineId&gt;.&lt;field&gt;</c>.</summary>
    public static string BurnPath(string engineId, string field) => $"{BurnPrefix}{engineId}.{field}";

    public bool TryGet(string path, out StateValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        switch (path)
        {
            case RepairInstall: value = repairs.LastInstalled; return true;
            case RepairPartial: value = repairs.LastPartial; return true;
            case EntryChecklistArmed: value = chutes.Armed; return true;
        }
        if (_burnPaths.TryGetValue(path, out var key))
        {
            value = (key.Docked, key.Field) switch
            {
                (false, DirectionField) => burns.DirectionDeg(key.Engine),
                (false, DurationField) => burns.DurationS(key.Engine),
                (false, _) => burns.TranslationBurning(key.Engine),
                (true, DurationField) => burns.DockedRemainingS(key.Engine, now()),
                _ => burns.DockedDeltaVMps(key.Engine),
            };
            return true;
        }
        if (path.StartsWith(RepairPrefix, StringComparison.Ordinal) && path.EndsWith(ReadySuffix, StringComparison.Ordinal) &&
            path.Length > RepairPrefix.Length + ReadySuffix.Length)
        {
            value = repairs.IsReady(path[RepairPrefix.Length..^ReadySuffix.Length]);
            return true;
        }
        value = StateValue.None;
        return false;
    }

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public bool CanApply(string target, EffectOperation operation)
    {
        if (operation != EffectOperation.Set) return false;
        if (target is RepairInstall or RepairPartial or EntryChecklistArmed) return true;
        return _burnPaths.TryGetValue(target, out var key) && (!key.Docked || key.Field == DurationField);
    }

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime time)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        switch (target)
        {
            case RepairInstall: repairs.Install(value.AsText(), time); return;
            case RepairPartial: repairs.Partial(value.AsText(), time); return;
            case EntryChecklistArmed: chutes.Armed = value.AsBool(); return;
        }
        var key = _burnPaths[target];
        if (key.Docked)
        {
            burns.StartDocked(key.Engine, value.AsNumber(), time);
            return;
        }
        switch (key.Field)
        {
            case DirectionField: burns.SetDirectionDeg(key.Engine, value.AsNumber()); break;
            case DurationField: burns.SetDurationS(key.Engine, value.AsNumber()); break;
            case StartField when value.AsBool(): burns.StartTranslation(key.Engine, time); break;
        }
    }

    /// <summary>Every burn path of the mission's engines, built once so reads never allocate.</summary>
    private static Dictionary<string, BurnPathKey> BurnPaths(BurnController burns)
    {
        var paths = new Dictionary<string, BurnPathKey>(StringComparer.Ordinal);
        foreach (var engine in burns.TranslationIds)
        {
            foreach (var field in (string[])[DirectionField, DurationField, StartField]) paths[BurnPath(engine, field)] = new BurnPathKey(engine, field, false);
        }
        foreach (var engine in burns.DockedIds)
        {
            foreach (var field in (string[])[DurationField, DockedDvField]) paths[BurnPath(engine, field)] = new BurnPathKey(engine, field, true);
        }
        return paths;
    }

    private readonly record struct BurnPathKey(string Engine, string Field, bool Docked);
}
