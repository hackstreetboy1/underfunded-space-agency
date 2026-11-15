using Usa.Game.Presentation.Map.Planning;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The planner's vehicle conditions as the ground knows them (X8, GDD 7.2): the crewed vehicle's reported attitude mode and
/// holding system (report facts reading <c>@report.&lt;crewed&gt;.attitude_mode</c> and <c>.control_set</c>), never the true
/// attitude mode. Before any report, or in a mission without those facts, the hold is unknown (null) and the planner assumes
/// nominal control. A vehicle reported free (no control) assumes the unattended pointing σ (<c>navigation.json</c>).
/// </summary>
public sealed class AttitudeHoldKnowledge
{
    /// <summary>The reported mode word for a vehicle no control holds.</summary>
    public const string ModeFree = "free";

    /// <summary>The reported control-set word when no system holds.</summary>
    public const string SetNone = "none";

    private readonly MissionSession _session;
    private readonly KnowledgeReadouts _readouts;
    private readonly FactId? _mode;
    private readonly FactId? _controlSet;
    private readonly double _unattendedPointingDeg;

    public AttitudeHoldKnowledge(MissionSession session, KnowledgeReadouts readouts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(readouts);
        _session = session;
        _readouts = readouts;
        var crewed = session.Data.Flight.Vehicles.CrewedId;
        _mode = ReportFacts.Resolve(session.Data.Facts, $"{crewed}.attitude_mode", $"{crewed}.attitude.mode");
        _controlSet = ReportFacts.Resolve(session.Data.Facts, $"{crewed}.control_set", $"{crewed}.attitude.control_set");
        _unattendedPointingDeg = session.Data.Navigation.PointingSigmaNoAttitudeDeg;
    }

    /// <summary>Whether the mission's catalog has the attitude report facts at all.</summary>
    public bool Available => _mode is not null || _controlSet is not null;

    public PlannerConditions Conditions()
    {
        var mode = ReportFacts.Latest(_session, _mode);
        var set = ReportFacts.Latest(_session, _controlSet);
        var modeWord = mode?.Qualitative;
        var setWord = set?.Qualitative;
        if (string.IsNullOrEmpty(modeWord) && string.IsNullOrEmpty(setWord)) return PlannerConditions.Nominal;

        if (mode is not null && string.Equals(modeWord, ModeFree, StringComparison.Ordinal))
            return new PlannerConditions(false, _unattendedPointingDeg, new AttitudeHold(Label(ModeFree), _readouts.SourceOf(mode), mode.Time));

        // The holding system names the hold (RCS A); without one the mode does (RATE DAMP). The newer report dates it.
        var latest = set is not null && (mode is null || set.Time > mode.Time) ? set : mode!;
        var label = !string.IsNullOrEmpty(setWord) && setWord != SetNone ? Label(setWord) : Label(modeWord ?? SetNone);
        return new PlannerConditions(true, 0, new AttitudeHold(label, _readouts.SourceOf(latest), latest.Time));
    }

    /// <summary><c>rcs_a</c> → <c>RCS A</c>.</summary>
    private static string Label(string word) => word.Replace('_', ' ').ToUpperInvariant();
}
