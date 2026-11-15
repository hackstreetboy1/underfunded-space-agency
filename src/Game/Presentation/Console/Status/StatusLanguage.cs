using Usa.Game.Content;
using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Presentation.Console.Status;

/// <summary>
/// Shape + word + colour token for every status the console shows (UI.md 2.2, 3.10); the words are the
/// <c>console.status*</c> strings. One shared instance per visual, so equal statuses compare equal and bindings
/// skip redundant updates.
/// </summary>
public sealed class StatusLanguage
{
    private readonly StatusVisual _degraded;
    private readonly StatusVisual _advisory;
    private readonly StatusVisual _master;
    private readonly StatusVisual _stable;
    private readonly StatusVisual _stressed;
    private readonly StatusVisual _impaired;
    private readonly StatusVisual _criticalHealth;
    private readonly StatusVisual _deceased;
    private readonly StatusVisual _objectiveOpen;
    private readonly StatusVisual _objectiveDone;
    private readonly StatusVisual _objectiveAtRisk;
    private readonly StatusVisual _objectiveImpossible;
    private readonly StatusVisual _objectiveAbandoned;
    private readonly StatusVisual _linkOk;
    private readonly StatusVisual _linkReconnecting;
    private readonly StatusVisual _covered;
    private readonly StatusVisual _atRisk;
    private readonly StatusVisual _notCovered;
    private readonly StatusVisual _noPlan;

    public StatusLanguage(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Nominal = new(StatusShape.FilledCircle, "●", text.Get("console.statusNominal"), ColourTokens.Ok);
        Caution = new(StatusShape.Triangle, "▲", text.Get("console.statusCaution"), ColourTokens.Caution);
        Warning = new(StatusShape.Octagon, "⯃", text.Get("console.statusWarning"), ColourTokens.Critical);
        Failed = new(StatusShape.Octagon, "⯃", text.Get("console.statusFailed"), ColourTokens.Critical);
        Isolated = new(StatusShape.SlashedSquare, "⧄", text.Get("console.statusIsolated"), ColourTokens.TextMid);
        Suspected = new(StatusShape.DashedTriangle, "△", text.Get("console.statusSuspected"), ColourTokens.Caution, Outlined: true);
        Unknown = new(StatusShape.DashedCircle, "◌", text.Get("console.statusUnknown"), ColourTokens.Unknown, Outlined: true);

        _degraded = new(StatusShape.Triangle, "▲", text.Get("console.statusDegraded"), ColourTokens.Caution);

        _advisory = new(StatusShape.InfoMark, "ⓘ", text.Get("console.alarmAdvisory"), ColourTokens.Info);
        _master = new(StatusShape.Octagon, "⯃", text.Get("console.alarmMaster"), ColourTokens.Critical);

        _stable = new(StatusShape.FilledCircle, "●", text.Get("console.healthStable"), ColourTokens.Ok);
        _stressed = new(StatusShape.Triangle, "▲", text.Get("console.healthStressed"), ColourTokens.Caution);
        _impaired = new(StatusShape.Octagon, "⯃", text.Get("console.healthImpaired"), ColourTokens.Critical);
        _criticalHealth = new(StatusShape.Octagon, "⯃", text.Get("console.healthCritical"), ColourTokens.Critical);
        _deceased = new(StatusShape.HollowCircle, "○", text.Get("console.healthDeceased"), ColourTokens.TextMid);

        _objectiveOpen = new(StatusShape.HollowCircle, "○", text.Get("console.objectiveOpen"), ColourTokens.TextMid);
        _objectiveDone = new(StatusShape.FilledCircle, "●", text.Get("console.objectiveDone"), ColourTokens.Ok);
        _objectiveAtRisk = new(StatusShape.Triangle, "▲", text.Get("console.objectiveAtRisk"), ColourTokens.Caution);
        _objectiveImpossible = new(StatusShape.Octagon, "⯃", text.Get("console.objectiveImpossible"), ColourTokens.Critical);
        _objectiveAbandoned = new(StatusShape.SlashedSquare, "⧄", text.Get("console.objectiveAbandoned"), ColourTokens.TextMid);

        _linkOk = new(StatusShape.FilledCircle, "●", text.Get("console.linkOk"), ColourTokens.Ok);
        _linkReconnecting = new(StatusShape.Triangle, "▲", text.Get("console.linkReconnecting"), ColourTokens.Caution);

        _covered = new(StatusShape.FilledCircle, "●", text.Get("console.coverageCovered"), ColourTokens.Ok);
        _atRisk = new(StatusShape.Triangle, "▲", text.Get("console.coverageAtRisk"), ColourTokens.Caution);
        _notCovered = new(StatusShape.Octagon, "⯃", text.Get("console.coverageNotCovered"), ColourTokens.Critical);
        _noPlan = new(StatusShape.HollowCircle, "○", text.Get("console.coverageNoPlan"), ColourTokens.TextMid);
    }

    public StatusVisual Nominal { get; }

    public StatusVisual Caution { get; }

    public StatusVisual Warning { get; }

    public StatusVisual Failed { get; }

    public StatusVisual Isolated { get; }

    public StatusVisual Suspected { get; }

    public StatusVisual Unknown { get; }

    /// <summary>The colour of an alarm priority, for rows that show no status word (the event log).</summary>
    public static string ColourToken(AlarmPriority priority) => priority switch
    {
        AlarmPriority.Advisory => ColourTokens.Info,
        AlarmPriority.Caution => ColourTokens.Caution,
        _ => ColourTokens.Critical,
    };

    public StatusVisual For(ConsoleStatus status) => status switch
    {
        ConsoleStatus.Nominal => Nominal,
        ConsoleStatus.Caution => Caution,
        ConsoleStatus.Warning => Warning,
        ConsoleStatus.Failed => Failed,
        ConsoleStatus.Isolated => Isolated,
        ConsoleStatus.Suspected => Suspected,
        _ => Unknown,
    };

    public StatusVisual For(ComponentCondition condition) => condition switch
    {
        ComponentCondition.Nominal => Nominal,
        ComponentCondition.Degraded => _degraded,
        ComponentCondition.Failed => Failed,
        ComponentCondition.Isolated => Isolated,
        ComponentCondition.Suspected => Suspected,
        _ => Unknown,
    };

    public StatusVisual For(AlarmPriority priority) => priority switch
    {
        AlarmPriority.Advisory => _advisory,
        AlarmPriority.Caution => Caution,
        AlarmPriority.Warning => Warning,
        _ => _master,
    };

    public StatusVisual For(CrewHealthBand band) => band switch
    {
        CrewHealthBand.Stable => _stable,
        CrewHealthBand.Stressed => _stressed,
        CrewHealthBand.Impaired => _impaired,
        CrewHealthBand.Critical => _criticalHealth,
        _ => _deceased,
    };

    public StatusVisual For(ObjectiveState state) => state switch
    {
        ObjectiveState.Completed => _objectiveDone,
        ObjectiveState.UnacceptableRisk => _objectiveAtRisk,
        ObjectiveState.Impossible => _objectiveImpossible,
        ObjectiveState.Abandoned => _objectiveAbandoned,
        _ => _objectiveOpen,
    };

    public StatusVisual For(LinkState link) => link == LinkState.Ok ? _linkOk : _linkReconnecting;

    public StatusVisual For(ForecastCoverage coverage) => coverage switch
    {
        ForecastCoverage.Covered => _covered,
        ForecastCoverage.AtRisk => _atRisk,
        ForecastCoverage.NotCovered => _notCovered,
        _ => _noPlan,
    };
}
