using System.Globalization;
using System.Text.RegularExpressions;
using Usa.Game.Presentation.Workspace;
using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The install task's attitude-rate preconditions from the task catalog (<c>abs(@attitude.&lt;vehicle&gt;.rate_&lt;axis&gt;_dps) &lt; N</c>),
/// each paired with the knowledge fact that holds the ground's reading of that rate: a fact whose value path is that state
/// path, else the vehicle's rate fact <c>&lt;vehicle&gt;.rate.&lt;axis&gt;_dps</c>. A rate with no such fact is left out (the
/// ground could not check it), so the workspace never judges a requirement on truth.
/// </summary>
public sealed partial class TaskRateRequirements : IRepairRequirementSource
{
    private readonly MissionSession _session;
    private readonly Dictionary<string, IReadOnlyList<RateRequirement>> _byTask = new(StringComparer.Ordinal);

    public TaskRateRequirements(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public IReadOnlyList<RateRequirement> RateLimits(RepairTaskSpec task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_byTask.TryGetValue(task.TaskId.Value, out var cached)) return cached;
        var limits = new List<RateRequirement>();
        if (_session.Data.Tasks.TryGet(task.TaskId, out var definition))
        {
            foreach (var precondition in definition.Preconditions)
            {
                if (Parse(precondition.Condition) is { } limit) limits.Add(limit);
            }
        }
        IReadOnlyList<RateRequirement> result = [.. limits];
        _byTask[task.TaskId.Value] = result;
        return result;
    }

    private RateRequirement? Parse(string condition)
    {
        var match = RateLimit().Match(condition);
        if (!match.Success) return null;
        var vehicle = match.Groups["vehicle"].Value;
        var axisWord = match.Groups["axis"].Value;
        if (!double.TryParse(match.Groups["limit"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var limitDps)) return null;
        var axis = axisWord switch
        {
            "roll" => RateAxis.Roll,
            "pitch" => RateAxis.Pitch,
            "yaw" => RateAxis.Yaw,
            _ => RateAxis.Total,
        };
        return RateFact(vehicle, axisWord) is { } fact ? new RateRequirement(axis, limitDps, fact.Value) : null;
    }

    private FactId? RateFact(string vehicle, string axis)
    {
        var path = $"attitude.{vehicle}.rate_{axis}_dps";
        FactId? byConvention = null;
        var conventional = $"{vehicle}.rate.{axis}_dps";
        foreach (var fact in _session.Data.Facts.Facts)
        {
            if (fact.ValuePath is { } valuePath && valuePath.TrimStart('@') == path) return fact.Id;
            if (fact.Id.Value == conventional) byConvention = fact.Id;
        }
        return byConvention;
    }

    [GeneratedRegex(@"abs\(\s*@attitude\.(?<vehicle>[A-Za-z0-9_\-]+)\.rate_(?<axis>roll|pitch|yaw|total)_dps\s*\)\s*<=?\s*(?<limit>[0-9]+(?:\.[0-9]+)?)")]
    private static partial Regex RateLimit();
}
