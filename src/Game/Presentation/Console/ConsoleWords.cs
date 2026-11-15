using Usa.Game.Content;
using Usa.Game.Presentation.Console.MainViews;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// The console's fixed words and sentence templates, from the <c>console.*</c> strings. Every fixed word is
/// resolved in the constructor, so a missing key fails when the console is built rather than mid-mission.
/// </summary>
public sealed class ConsoleWords
{
    private readonly ITextSource _text;
    private readonly string[] _phases;
    private readonly string[] _logCategories;
    private readonly string?[] _compressionCauses;
    private readonly string[] _loadPriorities;
    private readonly string[] _tabs;
    private readonly string[] _tipStages;
    private readonly string?[] _factBadges;
    private readonly string[] _views;

    public ConsoleWords(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        AgencyMark = text.Get("agency.headerMark");
        MasterAlarm = text.Get("console.masterAlarm");
        Idle = text.Get("console.crewIdle");
        Lost = text.Get("console.crewLost");
        SwitchOn = text.Get("console.switchOn");
        SwitchOff = text.Get("console.switchOff");
        SwitchUnknown = text.Get("console.switchUnknown");
        NothingDepends = text.Get("console.loadNothingDepends");
        NoActiveTask = text.Get("console.taskNone");
        CrewBusy = text.Get("console.taskCrewBusy");
        TipSignature = text.Get("console.tipSignature");
        TipAvailable = text.Get("console.tipAvailable");
        LegacyTapeLabel = text.Get("console.thermalTapeLabel");
        ControlRemote = text.Get("console.controlRemote");
        ControlGuarded = text.Get("console.controlGuarded");
        ControlCrewOnly = text.Get("console.controlCrewOnly");
        ControlExecutorRemote = text.Get("console.controlExecutorRemote");
        AlarmAck = text.Get("console.alarmAck");
        AlarmAckAll = text.Get("console.alarmAckAll");
        AlarmAckTooltip = text.Get("console.alarmAckTooltip");
        AlarmAckAllTooltip = text.Get("console.alarmAckAllTooltip");
        _views = Resolve<MainViewKind>(ViewKey);
        _phases = Resolve<MissionPhase>(PhaseKey);
        _logCategories = Resolve<LogCategory>(LogCategoryKey);
        _compressionCauses = ResolveOptional<CompressionCause>(CompressionCauseKey);
        _loadPriorities = Resolve<LoadPriorityGroup>(LoadPriorityKey);
        _tabs = Resolve<SystemsTab>(TabKey);
        _tipStages = ResolveOptional<TipStage>(TipStageKey).Select(word => word ?? string.Empty).ToArray();
        _factBadges = ResolveOptional<ReadoutKnowledge>(FactBadgeKey);
        foreach (var key in TemplateKeys) text.Get(key);
    }

    /// <summary><c>USA</c>, the header mark.</summary>
    public string AgencyMark { get; }

    public string MasterAlarm { get; }

    /// <summary>Activity line of a crew member with nothing to do.</summary>
    public string Idle { get; }

    /// <summary>A deceased astronaut's card when the time of death is not known.</summary>
    public string Lost { get; }

    /// <summary>Default position words of a switch without its own labels.</summary>
    public string SwitchOn { get; }

    public string SwitchOff { get; }

    public string SwitchUnknown { get; }

    public string NothingDepends { get; }

    public string NoActiveTask { get; }

    /// <summary>Busy flash when the slot's task is not known.</summary>
    public string CrewBusy { get; }

    public string TipSignature { get; }

    public string TipAvailable { get; }

    /// <summary>Tape label on the legacy (Thermal) panel.</summary>
    public string LegacyTapeLabel { get; }

    /// <summary>First part of a remote control's sub-line, <c>Remote</c>.</summary>
    public string ControlRemote { get; }

    /// <summary>Sub-line part of a covered (guarded) switch.</summary>
    public string ControlGuarded { get; }

    /// <summary>Sub-line of a crew-only control.</summary>
    public string ControlCrewOnly { get; }

    /// <summary>Who executes a remote control change, for the confirmation dialog.</summary>
    public string ControlExecutorRemote { get; }

    /// <summary>ACK on an alarm row when it acknowledges just that alarm.</summary>
    public string AlarmAck { get; }

    /// <summary>ACK on an alarm row when acknowledging acknowledges every active alarm.</summary>
    public string AlarmAckAll { get; }

    public string AlarmAckTooltip { get; }

    public string AlarmAckAllTooltip { get; }

    /// <summary>Main view tab name, <c>Orbit</c>.</summary>
    public string ViewLabel(MainViewKind view) => Lookup(_views, (int)view, (int)MainViewKind.Orbit);

    /// <summary>Sub-line part after a seen position change, <c>set OFF at 06:50</c>.</summary>
    public string ControlSetAt(string position, string met) => _text.Format("console.controlSetAt", ("position", position), ("met", met));

    public string PhaseWord(MissionPhase phase) => Lookup(_phases, (int)phase, (int)MissionPhase.DockedOps);

    public string CategoryWord(LogCategory category) => Lookup(_logCategories, (int)category, (int)LogCategory.Info);

    /// <summary>Null for <see cref="CompressionCause.None"/>.</summary>
    public string? CompressionReason(CompressionCause cause) =>
        (uint)cause < (uint)_compressionCauses.Length ? _compressionCauses[(int)cause] : null;

    public string LoadPriorityWord(LoadPriorityGroup group) => Lookup(_loadPriorities, (int)group, (int)LoadPriorityGroup.Discretionary);

    public string TabLabel(SystemsTab tab) => Lookup(_tabs, (int)tab, (int)SystemsTab.Inventory);

    /// <summary>Empty for <see cref="TipStage.None"/>.</summary>
    public string TipStageWord(TipStage stage) => (uint)stage < (uint)_tipStages.Length ? _tipStages[(int)stage] : string.Empty;

    /// <summary>Null for a known, fresh fact.</summary>
    public string? FactBadge(ReadoutKnowledge knowledge) =>
        (uint)knowledge < (uint)_factBadges.Length ? _factBadges[(int)knowledge] : _factBadges[(int)ReadoutKnowledge.Unknown];

    /// <summary><c>MET 06:58:12</c>.</summary>
    public string MetClock(string met) => _text.Format("console.metClock", ("met", met));

    /// <summary><c>LOST · MET 06:58</c>.</summary>
    public string LostAt(string met) => _text.Format("console.crewLostAt", ("met", met));

    /// <summary><c>1/3 done</c>.</summary>
    public string ObjectivesDone(int done, int total) =>
        _text.Format("console.objectivesDone", ("done", Invariant(done)), ("total", Invariant(total)));

    /// <summary>Default "Ask" pre-fill: <c>Read OAMS</c>.</summary>
    public string AskRead(string label) => _text.Format("console.askRead", ("label", label));

    /// <summary>Crew order for a switch whose position is unknown.</summary>
    public string ReportPosition(string label) => _text.Format("console.controlReportPosition", ("label", label));

    /// <summary><c>Switch OAMS master power OFF</c>.</summary>
    public string SwitchAction(string label, string position) =>
        _text.Format("console.controlSwitchAction", ("label", label), ("position", position));

    /// <summary><c>Depends on this: Cabin fan, Radio</c>.</summary>
    public string DependsOn(string dependents) => _text.Format("console.loadDependsOn", ("dependents", dependents));

    /// <summary><c>Crew busy: Okafor is on Cycle breakers</c>.</summary>
    public string CrewBusyOn(string assignee, string task) =>
        _text.Format("console.taskCrewBusyOn", ("assignee", assignee), ("task", task));

    /// <summary><c>ETA MET 07:12:30</c>.</summary>
    public string TaskEtaMet(string met) => _text.Format("console.taskEtaMet", ("met", met));

    /// <summary><c>≈ 24 s real</c>.</summary>
    public string TaskEtaReal(string duration) => _text.Format("console.taskEtaReal", ("duration", duration));

    /// <summary><c>Next tip in 4 min</c>.</summary>
    public string NextTip(string relative) => _text.Format("console.tipNext", ("relative", relative));

    private static readonly string[] TemplateKeys =
    [
        "console.metClock", "console.crewLostAt", "console.objectivesDone", "console.askRead", "console.controlReportPosition",
        "console.controlSwitchAction", "console.loadDependsOn", "console.taskCrewBusyOn", "console.taskEtaMet", "console.taskEtaReal",
        "console.tipNext", "console.controlSetAt",
    ];

    private static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Lookup(string[] words, int index, int fallback) => (uint)index < (uint)words.Length ? words[index] : words[fallback];

    private string[] Resolve<TEnum>(Func<TEnum, string> key)
        where TEnum : struct, Enum => ResolveOptional<TEnum>(value => key(value)).Select(word => word!).ToArray();

    // Indexed by the enum's integer value; the console enums are dense from zero.
    private string?[] ResolveOptional<TEnum>(Func<TEnum, string?> key)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var words = new string?[values.Length];
        foreach (var value in values)
        {
            var index = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            words[index] = key(value) is { } k ? _text.Get(k) : null;
        }
        return words;
    }

    private static string PhaseKey(MissionPhase phase) => phase switch
    {
        MissionPhase.Emergency => "console.phaseEmergency",
        MissionPhase.ReturnPrep => "console.phaseReturnPrep",
        MissionPhase.Entry => "console.phaseEntry",
        MissionPhase.Recovery => "console.phaseRecovery",
        MissionPhase.DockingOps => "console.phaseDockingOps",
        MissionPhase.Ended => "console.phaseEnded",
        MissionPhase.DockedOps => "console.phaseDockedOps",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    private static string LogCategoryKey(LogCategory category) => category switch
    {
        LogCategory.Alarm => "console.logAlarm",
        LogCategory.Crew => "console.logCrew",
        LogCategory.Command => "console.logCommand",
        LogCategory.SystemChange => "console.logSystem",
        LogCategory.Decision => "console.logDecision",
        _ => "console.logInfo",
    };

    private static string? CompressionCauseKey(CompressionCause cause) => cause switch
    {
        CompressionCause.CriticalAlarm => "console.compressionCriticalAlarm",
        CompressionCause.HighAngularRate => "console.compressionHighRate",
        CompressionCause.BurnInProgress => "console.compressionBurn",
        CompressionCause.Docking => "console.compressionDocking",
        CompressionCause.EntryBelowInterface => "console.compressionEntry",
        CompressionCause.ParachutePhase => "console.compressionParachutes",
        CompressionCause.EventApproaching => "console.compressionEventAhead",
        CompressionCause.WorseningCondition => "console.compressionWorsening",
        CompressionCause.CrewTaskRunning => "console.compressionCrewTask",
        CompressionCause.StableCoast => "console.compressionStableCoast",
        _ => null,
    };

    private static string LoadPriorityKey(LoadPriorityGroup group) => group switch
    {
        LoadPriorityGroup.Survival => "console.loadSurvival",
        LoadPriorityGroup.Return => "console.loadReturn",
        LoadPriorityGroup.Support => "console.loadSupport",
        _ => "console.loadDiscretionary",
    };

    private static string TabKey(SystemsTab tab) => tab switch
    {
        SystemsTab.Power => "console.tabPower",
        SystemsTab.LifeSupport => "console.tabLifeSupport",
        SystemsTab.PropAttitude => "console.tabPropAttitude",
        SystemsTab.Thermal => "console.tabThermal",
        SystemsTab.Navigation => "console.tabNavigation",
        SystemsTab.StructureLanding => "console.tabStructureLanding",
        _ => "console.tabInventory",
    };

    private static string ViewKey(MainViewKind view) => view switch
    {
        MainViewKind.External => "console.viewExternal",
        MainViewKind.Diagram => "console.viewDiagram",
        MainViewKind.Workspace => "console.viewWorkspace",
        _ => "console.viewOrbit",
    };

    private static string? TipStageKey(TipStage stage) => stage switch
    {
        TipStage.Nudge => "console.tipStageNudge",
        TipStage.Investigate => "console.tipStageInvestigate",
        TipStage.Connect => "console.tipStageConnect",
        TipStage.Recommend => "console.tipStageRecommend",
        TipStage.Direct => "console.tipStageDirect",
        _ => null,
    };

    private static string? FactBadgeKey(ReadoutKnowledge knowledge) => knowledge switch
    {
        ReadoutKnowledge.Known => null,
        ReadoutKnowledge.Stale => "console.factStale",
        ReadoutKnowledge.Estimated => "console.factEstimated",
        ReadoutKnowledge.Conflict => "console.factConflict",
        ReadoutKnowledge.GroundBand => "console.factGroundBand",
        _ => "console.factUnknown",
    };
}
