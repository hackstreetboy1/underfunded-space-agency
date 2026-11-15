using Usa.Game.Content;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// Fixed labels of the console, comms and External view chrome scenes from <c>content/strings.json</c> (<c>console.*</c>,
/// <c>externalView.*</c>), resolved once per mission so a missing key fails when the console binds. The scenes keep the
/// same words as design text for previews that bind no strings.
/// </summary>
public sealed class ConsoleLabels
{
    private readonly ITextSource _text;

    public ConsoleLabels(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        PttListening = text.Get("console.pttListening");
        PttTranscribing = text.Get("console.pttTranscribing");
        PttReview = text.Get("console.pttReview");
        PttDisabled = text.Get("console.pttDisabled");
        CrewTalk = text.Get("console.crewTalk");
        AddresseeCrew = text.Get("console.addresseeCrew");
        EditResend = text.Get("console.editResend");
        ReportBadge = text.Get("console.reportBadge");
        TipStronger = text.Get("console.tipStronger");
        TipNoGradeNote = text.Get("console.tipNoGradeNote");
        TipDismiss = text.Get("console.tipDismiss");
        TipAvailable = text.Get("console.tipAvailable");
        AdvanceNextEvent = text.Get("console.advanceNextEvent");
        AdvanceShort = text.Get("console.advanceShort");
        AdvanceKey = text.Get("console.advanceKey");
        HeaderObjectives = text.Get("console.headerObjectives");
        HeaderMenu = text.Get("console.headerMenu");
        MasterAlarm = text.Get("console.masterAlarm");
        MasterAlarmKey = text.Get("console.masterAlarmKey");
        AlarmAckTooltip = text.Get("console.alarmAckTooltip");
        Ask = text.Get("console.ask");
        ForecastTitle = text.Get("console.forecastTitle");
        ForecastBreakdownTitle = text.Get("console.forecastBreakdownTitle");
        ForecastAssumptions = text.Get("console.forecastAssumptions");
        ForecastLimitingBadge = text.Get("console.forecastLimitingBadge");
        ForecastClose = text.Get("console.forecastClose");
        ResourceCrew = text.Get("console.resourceCrew");
        LogTitle = text.Get("console.logTitle");
        LogFilterAll = text.Get("console.logFilterAll");
        LogFilterAlarms = text.Get("console.logFilterAlarms");
        LogFilterCrew = text.Get("console.logFilterCrew");
        LogFilterCommands = text.Get("console.logFilterCommands");
        LogFilterSystemChanges = text.Get("console.logFilterSystemChanges");
        LogFilterDecisions = text.Get("console.logFilterDecisions");
        LogSearch = text.Get("console.logSearch");
        LogFilterCompact(string.Empty);
        CommsTitle = text.Get("console.commsTitle");
        CommsSearchHint = text.Get("console.commsSearchHint");
        CommsSearch = text.Get("console.commsSearch");
        CommsAddresseeTooltip = text.Get("console.commsAddresseeTooltip");
        CommsInput = text.Get("console.commsInput");
        CommsSend = text.Get("console.commsSend");
        CommsYou = text.Get("console.commsYou");
        CommsVoice = text.Get("console.commsVoice");
        CommsRefused = text.Get("console.commsRefused");
        CommsCancelRequest = text.Get("console.commsCancelRequest");
        CommsMore = text.Get("console.commsMore");
        PttUndo(0);
        SystemsTitle = text.Get("console.systemsTitle");
        SystemsLegacyAside = text.Get("console.systemsLegacyAside");
        SystemsLoads = text.Get("console.systemsLoads");
        SystemsRemoteControls = text.Get("console.systemsRemoteControls");
        SystemsRemoteAside = text.Get("console.systemsRemoteAside");
        ControlRequest = text.Get("console.controlRequest");
        ControlCrewMarker = text.Get("console.controlCrewMarker");
        ControlCrewMarkerTooltip = text.Get("console.controlCrewMarkerTooltip");
        GuardedCover = text.Get("console.guardedCover");
        GuardedOpen = text.Get("console.guardedOpen");
        GuardedClose = text.Get("console.guardedClose");
        GuardedAwaiting = text.Get("console.guardedAwaiting");
        CrewSpeaking = text.Get("console.crewSpeaking");
        CrewFatigue = text.Get("console.crewFatigue");
        CrewHydration = text.Get("console.crewHydration");
        ConfirmIrreversible = text.Get("console.confirmIrreversible");
        ConfirmUnsafeToStop = text.Get("console.confirmUnsafeToStop");
        ConfirmFrozen = text.Get("console.confirmFrozen");
        ConfirmCancel = text.Get("console.confirmCancel");
        ConfirmConfirm = text.Get("console.confirmConfirm");
        ConfirmTimer(0);
        ConfirmExecutor(string.Empty);
        TaskComplexity = text.Get("console.taskComplexity");
        TaskStop = text.Get("console.taskStop");
        ExternalReset = text.Get("externalView.reset");
        ExternalFocusVehicle = text.Get("externalView.focusVehicle");
        ExternalFocusComponent = text.Get("externalView.focusComponent");
        ExternalFocusLastRevealed = text.Get("externalView.focusLastRevealed");
        LegendConfirmedNominal = text.Get("externalView.legendConfirmedNominal");
        LegendConfirmedDamage = text.Get("externalView.legendConfirmedDamage");
        LegendUnconfirmed = text.Get("externalView.legendUnconfirmed");
        LegendOlder = text.Get("externalView.legendOlder");
        FirstUseLegend = text.Get("externalView.firstUseLegend");
        FirstUseLegendDetail = text.Get("externalView.firstUseLegendDetail");
        FirstUseDismiss = text.Get("externalView.firstUseDismiss");
        DiagramLayerPower = text.Get("console.diagramLayerPower");
        DiagramLayerFluids = text.Get("console.diagramLayerFluids");
        DiagramLayerPropellant = text.Get("console.diagramLayerPropellant");
        DiagramLayerData = text.Get("console.diagramLayerData");
        DiagramFit = text.Get("console.diagramFit");
        DiagramOpenWorkspace = text.Get("console.diagramOpenWorkspace");
        DiagramNotConfirmed = text.Get("externalView.notConfirmed");
        DiagramAskInspect = text.Get("externalView.askCrewToInspect");
        DiagramConfirmed(string.Empty, string.Empty);
        DiagramInspectPrefill(string.Empty);
        PttIdle(string.Empty);
    }

    public string PttListening { get; }

    public string PttTranscribing { get; }

    /// <summary>PTT bar while a low-confidence transcript waits in the input.</summary>
    public string PttReview { get; }

    /// <summary>PTT bar with the microphone off in settings.</summary>
    public string PttDisabled { get; }

    /// <summary>The crew card's Talk button.</summary>
    public string CrewTalk { get; }

    /// <summary>The addressee selector's "everyone" entry.</summary>
    public string AddresseeCrew { get; }

    /// <summary>On a player bubble: puts its text back in the input.</summary>
    public string EditResend { get; }

    /// <summary>Badge of an automatic crew observation report.</summary>
    public string ReportBadge { get; }

    public string TipStronger { get; }

    public string TipNoGradeNote { get; }

    public string TipDismiss { get; }

    public string TipAvailable { get; }

    public string AdvanceNextEvent { get; }

    /// <summary>Advance button label in the compact layout.</summary>
    public string AdvanceShort { get; }

    /// <summary>The advance key hint, <c>N</c>.</summary>
    public string AdvanceKey { get; }

    public string HeaderObjectives { get; }

    public string HeaderMenu { get; }

    public string MasterAlarm { get; }

    /// <summary>The acknowledge key hint under the master alarm, <c>Ctrl+A</c>.</summary>
    public string MasterAlarmKey { get; }

    public string AlarmAckTooltip { get; }

    /// <summary>The "Ask" affordance of an unknown value.</summary>
    public string Ask { get; }

    public string ForecastTitle { get; }

    public string ForecastBreakdownTitle { get; }

    public string ForecastAssumptions { get; }

    /// <summary>Badge of the resource that sets the safe autonomy, in the breakdown.</summary>
    public string ForecastLimitingBadge { get; }

    public string ForecastClose { get; }

    /// <summary>The Crew tile's label.</summary>
    public string ResourceCrew { get; }

    public string LogTitle { get; }

    public string LogFilterAll { get; }

    public string LogFilterAlarms { get; }

    public string LogFilterCrew { get; }

    public string LogFilterCommands { get; }

    public string LogFilterSystemChanges { get; }

    public string LogFilterDecisions { get; }

    /// <summary>Search field placeholder with its key hint.</summary>
    public string LogSearch { get; }

    public string CommsTitle { get; }

    public string CommsSearchHint { get; }

    public string CommsSearch { get; }

    public string CommsAddresseeTooltip { get; }

    public string CommsInput { get; }

    public string CommsSend { get; }

    /// <summary>The player in a bubble's meta line.</summary>
    public string CommsYou { get; }

    /// <summary>Marks a spoken message.</summary>
    public string CommsVoice { get; }

    public string CommsRefused { get; }

    public string CommsCancelRequest { get; }

    /// <summary>The compact layout's quick-command overflow chip.</summary>
    public string CommsMore { get; }

    public string SystemsTitle { get; }

    /// <summary>Aside of the Thermal tab's legacy panel.</summary>
    public string SystemsLegacyAside { get; }

    public string SystemsLoads { get; }

    public string SystemsRemoteControls { get; }

    /// <summary>Aside of the remote controls section: who operates them.</summary>
    public string SystemsRemoteAside { get; }

    public string ControlRequest { get; }

    public string ControlCrewMarker { get; }

    public string ControlCrewMarkerTooltip { get; }

    public string GuardedCover { get; }

    public string GuardedOpen { get; }

    public string GuardedClose { get; }

    public string GuardedAwaiting { get; }

    public string CrewSpeaking { get; }

    public string CrewFatigue { get; }

    public string CrewHydration { get; }

    public string ConfirmIrreversible { get; }

    public string ConfirmUnsafeToStop { get; }

    public string ConfirmFrozen { get; }

    public string ConfirmCancel { get; }

    public string ConfirmConfirm { get; }

    public string TaskComplexity { get; }

    public string TaskStop { get; }

    public string ExternalReset { get; }

    public string ExternalFocusVehicle { get; }

    public string ExternalFocusComponent { get; }

    public string ExternalFocusLastRevealed { get; }

    public string LegendConfirmedNominal { get; }

    public string LegendConfirmedDamage { get; }

    public string LegendUnconfirmed { get; }

    public string LegendOlder { get; }

    public string FirstUseLegend { get; }

    public string FirstUseLegendDetail { get; }

    public string FirstUseDismiss { get; }

    public string DiagramLayerPower { get; }

    public string DiagramLayerFluids { get; }

    public string DiagramLayerPropellant { get; }

    public string DiagramLayerData { get; }

    public string DiagramFit { get; }

    public string DiagramOpenWorkspace { get; }

    public string DiagramNotConfirmed { get; }

    public string DiagramAskInspect { get; }

    /// <summary><c>Confirmed: Okafor · MET 06:58</c>.</summary>
    public string DiagramConfirmed(string who, string met) => _text.Format("console.diagramConfirmed", ("who", who), ("met", met));

    /// <summary>The crew order "Ask crew to inspect" places in the comms input, <c>Inspect OAMS thruster 8</c>.</summary>
    public string DiagramInspectPrefill(string name) => _text.Format("console.diagramInspectPrefill", ("name", name));

    /// <summary><c>HOLD SPACE TO TALK</c>.</summary>
    /// <param name="keyName">The push-to-talk key as the player bound it.</param>
    public string PttIdle(string keyName) => _text.Format("console.pttIdle", ("key", keyName));

    /// <summary>The chip after a voice order was sent without review, <c>Undo (2 s)</c>.</summary>
    public string PttUndo(int seconds) =>
        _text.Format("console.pttUndo", ("seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The compact event log's filter menu, <c>Filter: All</c>.</summary>
    public string LogFilterCompact(string filter) => _text.Format("console.logFilterCompact", ("filter", filter));

    /// <summary><c>Auto-cancels in 20 s</c>.</summary>
    public string ConfirmTimer(double seconds) =>
        _text.Format("console.confirmTimer", ("seconds", seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary><c>Executes: Cal Brandt</c>.</summary>
    public string ConfirmExecutor(string who) => _text.Format("console.confirmExecutor", ("who", who));
}
