namespace Usa.Game.UI.Frontend.Model;

/// <summary>
/// Copy from the approved Gate 6 frames (Figma page <c>60 Front-end (Set 6)</c>) that <c>content/strings.json</c> does not
/// carry yet. Keys use the sections of that file (debrief extras use a new <c>debrief</c> prefix); the content owner moves them
/// into data, after which these entries are never read.
/// </summary>
public static class FrontendTextDefaults
{
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Shared
        ["settings.on"] = "ON",
        ["settings.off"] = "OFF",
        ["mainMenu.back"] = "Main menu",

        // G6-01 Main menu
        ["mainMenu.continueLine"] = "{mission} · {difficulty} · resumes at MET {met}",
        ["mainMenu.keysHint"] = "↑ ↓ to move · Enter to select · Esc returns here",
        ["mainMenu.abortNotice"] = "The last flight ended without a debrief. It was not graded.",

        // G6-02 Mission select
        ["missionSelect.missionNumber"] = "Mission {number}",
        ["missionSelect.objectivesTitle"] = "Flight objectives",
        ["missionSelect.openBriefing"] = "Open briefing",
        ["missionSelect.scoreOutOf"] = "{points} / 100",
        ["missionSelect.splusAchieved"] = "S+ achieved",
        ["missionSelect.objectivesCompleted"] = "{completed} of {total} objectives completed",
        ["missionSelect.flyingOn"] = "Flying on {difficulty}",
        ["missionSelect.difficultyHint.easy"] = "Easy: tips come sooner and go further, the burn planner proposes burns, extra spares.",
        ["missionSelect.difficultyHint.normal"] = "Normal: tips escalate in layers, burn preview with markers, standard spares.",
        ["missionSelect.hardArrives"] = "Arrives with the campaign",

        // G6-03 Briefing
        ["briefing.back"] = "Missions",
        ["briefing.premiseTitle"] = "Premise",
        ["briefing.crewAge"] = "{role} · age {age}",
        ["briefing.previous"] = "Previous",
        ["briefing.next"] = "Next",
        ["briefing.imageCounter"] = "{index} / {count}",
        ["briefing.credit"] = "Credit: {credit}",
        ["briefing.altText"] = "Alt: {alt}",
        ["briefing.imageNotCached"] = "Image not cached yet",
        ["briefing.noPause"] = "The flight does not pause once it begins.",
        ["briefing.headerLine"] = "{agency} · {vehicles} · {era} · {orbit} · starts at {start}",

        // G6-10 Loading
        ["briefing.loadingTitle"] = "Loading mission",
        ["briefing.loadingStatus"] = "Loading the flight data",
        ["briefing.loadingSource"] = "Source: {source}",

        // G6-04 Debrief (extras beyond debrief.json labels)
        ["debrief.title"] = "Debrief · {mission}",
        ["debrief.meta"] = "{difficulty} · seed {seed}",
        ["debrief.band"] = "Band {grade} · {range}",
        ["debrief.bandGated"] = "{range}, gated",
        ["debrief.finalSuffix"] = "/ 100 final",
        ["debrief.raw"] = "Raw score {raw}",
        ["debrief.dimensionsTitle"] = "Score dimensions",
        ["debrief.objectivesTitle"] = "Objectives",
        ["debrief.returnCompleted"] = "Completed",
        ["debrief.returnCrewHome"] = "{home} of {count} crew home",
        ["debrief.returnNotLanded"] = "Not landed",
        ["debrief.realHistoryTitle"] = "Real history",
        ["debrief.jumpTo"] = "Jump to",
        ["debrief.knownLine"] = "{label}: {items}",
        ["debrief.crewLost"] = "LOST",

        // G6-05 Settings
        ["settings.title"] = "Settings",
        ["settings.applyNote"] = "Changes apply immediately",
        ["settings.restoreDefaults"] = "Restore defaults",
        ["settings.audioMaster"] = "Master",
        ["settings.audioSpeech"] = "Speech",
        ["settings.audioSpeechHint"] = "Crew voices and the flight director.",
        ["settings.audioAlarms"] = "Alarms",
        ["settings.audioAlarmsHint"] = "Caution and warning tones.",
        ["settings.audioSfx"] = "SFX",
        ["settings.audioAmbience"] = "Ambience",
        ["settings.audioAmbienceHint"] = "Cabin and ground-room bed; ducks under speech.",
        ["settings.audioUi"] = "UI",
        ["settings.captionsSpeakerNames"] = "Speaker names",
        ["settings.captionSize"] = "Caption size",
        ["settings.captionSizeHint"] = "18–32 px",
        ["settings.captionOpacity"] = "Background opacity",
        ["settings.captionOpacityHint"] = "Box behind the caption text.",
        ["settings.captionPreviewTitle"] = "Preview",
        ["settings.captionPreviewSpeaker"] = "OKAFOR",
        ["settings.captionPreviewLine"] = "OAMS reads two-twelve. Below the plan line.",
        ["settings.uiScale"] = "UI scale",
        ["settings.uiScaleHint"] = "80–150 %. Text never goes below 12 px effective.",
        ["settings.highContrastHint"] = "Black panels, white text, 2 px borders, solid badges.",
        ["settings.reducedMotionHint"] = "No camera shake, softer reveals, shorter transitions.",
        ["settings.windowModeWindowed"] = "Windowed",
        ["settings.windowModeFullscreen"] = "Fullscreen",
        ["settings.windowModeExclusive"] = "Exclusive fullscreen",
        ["settings.rebind"] = "Rebind",
        ["settings.cancelRebind"] = "Cancel",
        ["settings.pressKey"] = "Press a key…",
        ["settings.controlsNote"] = "PTT is ignored while a text field has focus. Esc while rebinding cancels; a key already in use is refused.",
        ["settings.microphone"] = "Microphone",
        ["settings.microphoneHint"] = "Turn it off at any time; the comms input bar works by text alone.",
        ["settings.inputDeviceHint"] = "Used only while you hold the push-to-talk key.",
        ["settings.pushToTalkHint"] = "Rebind under Controls.",
        ["settings.backendUrl"] = "Local proxy address",
        ["settings.backendUrlHint"] = "Must be on this computer. Press Enter to apply.",
        ["settings.tutorialCallouts"] = "Tutorial callouts",
        ["settings.tutorialCalloutsHint"] = "Short anchored callouts in the calm opening. They never pause the flight.",
        ["settings.tipFrequency"] = "Tip frequency",
        ["settings.tipFrequencyHint"] = "Easy: fast. Normal: layered. Hard (campaign): slow, no unsolicited solutions. Tips never affect the grade.",
        ["settings.action.ptt"] = "Push-to-talk (hold)",
        ["settings.action.focus_comms"] = "Focus the comms input",
        ["settings.action.view_1"] = "Orbit view",
        ["settings.action.view_2"] = "External view",
        ["settings.action.view_3"] = "Diagram view",
        ["settings.action.view_4"] = "Workspace view",
        ["settings.action.ack_alarms"] = "Acknowledge alarms",
        ["settings.action.search"] = "Search transcript and log",
        ["settings.action.focus_vehicle"] = "Focus vehicle",
        ["settings.action.advance_event"] = "Advance to next event",
        ["settings.action.encyclopedia"] = "Encyclopedia sheet",
        ["settings.action.menu"] = "Close dialog or sheet, else Esc menu",

        // G6-06 Esc menu
        ["escMenu.keysHint"] = "Esc resumes · Restart and Abandon ask first",
        ["escMenu.frozenTag"] = "frozen",
        ["escMenu.effectsTitle"] = "Effects",
        ["escMenu.frozenEffect"] = "Mission time, tasks and resources stay frozen until you choose.",
        ["escMenu.noAutoCancel"] = "No auto-cancel while frozen",

        // G6-07 Encyclopedia
        ["encyclopedia.simRunning"] = "Sim running",
        ["encyclopedia.closeHint"] = "F1 / Esc closes",
        ["encyclopedia.listTitle"] = "{category} · {count} entries",
        ["encyclopedia.related"] = "Related",
        ["encyclopedia.askHint"] = "Explains how things work · never what to do",
        ["encyclopedia.noImage"] = "Diagram drawn in-house; no curated image for this entry.",
        ["encyclopedia.empty"] = "No entry matches the search.",
        ["encyclopedia.pickEntry"] = "Pick an entry on the left.",

        // G6-08 Tutorial callouts
        ["briefing.calloutHeader"] = "Guided first run",
        ["briefing.calloutGotIt"] = "Got it",

        // G6-09 Credits
        ["credits.escHint"] = "Esc returns to the menu",
    };
}
