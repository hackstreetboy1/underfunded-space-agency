using System.Text.Json;
using Godot;
using Usa.Game.Comms;
using Usa.Game.Flow;
using Usa.Game.Flow.Tutorial;
using Usa.Game.Presentation.Console.MainViews;
using Usa.Game.UI.Console;
using Usa.Game.UI.Console.Comms;
using Usa.Game.UI.Console.Components;
using Usa.Game.UI.Console.Overlays;
using Usa.Game.UI.Console.Regions;
using Usa.Game.UI.Frontend;
using Usa.Simulation.Session.Scripts;

namespace Usa.Game.Diagnostics;

/// <summary>
/// <c>--guided=1</c>: a fresh guided first run played the way a player follows the guide. Every input is a mouse click or
/// key press pushed through <see cref="Godot.Input.ParseInputEvent"/> at the control's on-screen position: the callout's
/// "Send: ‘…’" button (then Confirm when the order asks for it), the control under the highlight ring (tab, planner
/// button, dropdown then its item, Propose, Execute, Confirm, Proceed), or "Continue" on informational callouts. Each
/// callout is captured once it settles (<c>NN_&lt;beat&gt;.png</c>) with its geometry logged. While the first callout waits,
/// a comms check types a question to June, answers her offer with "yes" and presses a resource tile's Ask.
/// </summary>
public sealed partial class PlaythroughDriver
{
    private const double GuidedTimeoutS = 3600;
    private const double BeatSettleS = 0.5;
    private const double BeatStuckS = 180;
    private const string TutorialPath = "res://data/missions/out_of_control/tutorial.json";

    private enum GuidedAct { Settle, Comms, Probe, Act, Wait, Passive, Done }

    // Off-script probes on the rails: at each of these steps, before acting, one typed order the step does not allow and a
    // click on a greyed-out control. Neither may submit a command or move the step.
    private static readonly Dictionary<string, string> ProbeOrders = new(StringComparer.Ordinal)
    {
        ["walk_dock"] = "Cal, fire the retros now",
        ["walk_open_t8"] = "Cal, undock now",
        ["walk_retro_propose"] = "Cal, run the entry checklist",
    };

    private sealed class GuidedState
    {
        public List<string> Seen { get; } = [];
        public string? Beat { get; set; }
        public double BeatS { get; set; }
        public double ActS { get; set; }
        public long BeatMetMs { get; set; }
        public GuidedAct Act { get; set; }
        public int Tries { get; set; }
        public bool ConfirmShot { get; set; }
        public int CommsStep { get; set; }
        public double CommsS { get; set; }
        public int CommsMark { get; set; }
        public bool CommsDone { get; set; }
        public (OptionButton Option, string Wanted, double AtS, string Beat)? Dropdown { get; set; }
        public double ProceedSeenS { get; set; } = -1;
        public double ConfirmSeenS { get; set; } = -1;
        public int ProbeStep { get; set; }
        public double ProbeS { get; set; }
        public double ProbeStartS { get; set; }
        public int ProbeMark { get; set; }
        public int ProbeSubmitMark { get; set; }
        public string ProbeStepBefore { get; set; } = "";
        public HashSet<string> Probed { get; } = new(StringComparer.Ordinal);
        public double ProbeTotalS { get; set; }
        public List<string> Submits { get; } = [];
        public bool Subscribed { get; set; }
        public double ClockS { get; set; }
        public Dictionary<string, double> StepTimes { get; } = new(StringComparer.Ordinal);
        public double FirstBeatS { get; set; } = -1;
        public string? CurrentStep { get; set; }
        public double CurrentStepS { get; set; }
        public int DockShots { get; set; }
        public double DockShotS { get; set; }
        public int ChuteShots { get; set; }
        public double ChuteShotS { get; set; }
        public int AdvanceStep { get; set; }
        public double AdvanceS { get; set; }
        public double AdvanceMaxRate { get; set; }
        public string AdvanceMaxText { get; set; } = "";
        public bool AdvanceShot { get; set; }
        public double ExternalKeyS { get; set; } = -10;
    }

    private void EnqueueGuidedRun(ScenarioScript script)
    {
        var router = _root.Services.Router;
        var zone = script.Commands.FirstOrDefault(c => c.Action == "retrofire")?.Zone ?? "";
        var zoneWord = zone.Split('_').FirstOrDefault() ?? "";
        var caps = WalkCaps();
        var s = new GuidedState();
        var clockS = 0.0;
        _stages.Enqueue(new Stage(
            "guided first run (player clicks)",
            () =>
            {
                Engine.TimeScale = _guidedSpeed;
                return router.OpenMissionSelect() && router.SelectMission(new MissionSelection(script.Mission, script.Difficulty, script.Seed))
                    && router.StartMission(guidedFirstRun: true);
            },
            () =>
            {
                if (router.Current == Screen.Debrief) return true;
                clockS += GetProcessDeltaTime() / Math.Max(Engine.TimeScale, 1e-6);
                // Not InConsole(): the session stops at recovery, and the player still has the guide and Proceed to press.
                if (router.Current != Screen.Mission || _root.Services.Flow.Context is not { } context || FindConsole() is not { Visible: true } console) return false;
                GuidedFrame(console, context, s, clockS, zoneWord, caps);
                return false;
            },
            onReady: () =>
            {
                Engine.TimeScale = 1;
                if (s.CurrentStep is { } last) s.StepTimes[last] = s.StepTimes.GetValueOrDefault(last) + (clockS - s.CurrentStepS);
                GD.Print($"[Play] guided timing: TOTAL {clockS:0.0} s real from Start to debrief (first callout at {s.FirstBeatS:0.0} s); " +
                         $"probes took {s.ProbeTotalS:0.0} s, so {clockS - s.ProbeTotalS:0.0} s without them (reaction {_guidedReactionS:0.0} s, Engine x{_guidedSpeed:0.#})");
                var number = 0;
                foreach (var (step, seconds) in s.StepTimes) GD.Print($"[Play] guided timing: step {++number:00} {step}: {seconds:0.0} s");
                GD.Print($"[Play] guided submissions ({s.Submits.Count}): {string.Join(" ; ", s.Submits)}");
                var missing = caps.Keys.Where(k => !s.Seen.Contains(k)).ToArray();
                GD.Print($"[Play] guided: {s.Seen.Count} beats shown: {string.Join(", ", s.Seen)}");
                if (missing.Length > 0) Fail($"guided: walkthrough beats never shown: {string.Join(", ", missing)}");
                else GD.Print("[Play] guided: every walk_* beat was shown");
            },
            capture: "99_debrief",
            timeoutS: GuidedTimeoutS));
    }

    private void GuidedFrame(MissionConsole console, Usa.Game.Integration.Runtime.MissionRuntimeContext context, GuidedState s, double clockS, string zoneWord, Dictionary<string, double?> caps)
    {
        var host = _root.Services.Flow.Host;
        var now = host.LastFrame.MetAfter.Milliseconds;
        var view = GetTree().CurrentScene?.FindChildren("*", "", true, false).OfType<TutorialCalloutView>().FirstOrDefault(c => c.IsVisibleInTree());
        var beat = view?.CurrentBeatId;

        // An opened dropdown: pick the item through its popup, whatever the guide shows meanwhile.
        if (s.Dropdown is { } drop && clockS - drop.AtS > 0.5)
        {
            s.Dropdown = null;
            PickFromPopup(drop.Option, drop.Wanted, $"{s.Seen.Count:00}p_{drop.Beat}_dropdown");
        }

        s.ClockS = clockS;
        if (!s.Subscribed)
        {
            s.Subscribed = true;
            context.CommandSubmitted += (request, result) =>
            {
                var line = $"{s.ClockS:0.0}s {s.Beat ?? "-"} {request.Action.Value} -> {result.Status}{(result.ReasonCode is { } why ? $" ({why})" : "")}";
                s.Submits.Add(line);
                GD.Print($"[Play] submitted: {line}");
            };
        }

        if (beat != s.Beat)
        {
            if (s.Beat is not null)
            {
                GD.Print($"[Play] guide: '{s.Beat}' closed after {clockS - s.BeatS:0.0} s real, MET {Met(s.BeatMetMs)} -> {host.LastFrame.MetAfter}");
            }
            if (beat is not null && s.FirstBeatS < 0) s.FirstBeatS = clockS;
            // A step's time runs from its callout to the next step's callout (its wait and retry callouts included).
            if (beat is not null && StepOf(beat) != s.CurrentStep)
            {
                if (s.CurrentStep is { } previous) s.StepTimes[previous] = s.StepTimes.GetValueOrDefault(previous) + (clockS - s.CurrentStepS);
                s.CurrentStep = StepOf(beat);
                s.CurrentStepS = clockS;
            }
            s.Beat = beat;
            s.BeatS = clockS;
            s.BeatMetMs = now;
            s.Act = GuidedAct.Settle;
            s.Tries = 0;
            s.ConfirmShot = false;
            s.ConfirmSeenS = -1;
            if (beat is not null)
            {
                s.Seen.Add(beat);
                GD.Print($"[Play] guide beat {s.Seen.Count} '{beat}' at {host.LastFrame.MetAfter}");
            }
        }

        WatchMoments(console, s, clockS, beat);

        if (beat is null || view is null)
        {
            ProceedWithoutGuide(console, clockS, s);
            return;
        }

        var hit = view.CurrentHit;
        var since = clockS - s.BeatS;
        var order = view.GetNode<Button>("%Order");
        var gotIt = view.GetNode<Button>("%GotIt");
        switch (s.Act)
        {
            case GuidedAct.Settle:
                if (since < BeatSettleS || hit is null) break;
                LogBeat(view, hit.Value, beat, caps, host.EffectiveRate, s.Seen.Count);
                Capture($"{s.Seen.Count:00}_{beat}");
                s.Act = _guidedCommsCheck && beat == "walk_dock" && !s.CommsDone ? GuidedAct.Comms
                    : ProbeOrders.ContainsKey(beat) && !s.Probed.Contains(beat) ? GuidedAct.Probe
                    : GuidedAct.Act;
                s.ActS = clockS;
                if (s.Act == GuidedAct.Probe) s.ProbeStartS = clockS;
                break;
            case GuidedAct.Probe:
                if (RailsProbe(console, context, s, clockS, beat))
                {
                    s.ProbeTotalS += clockS - s.ProbeStartS;
                    s.ProbeStep = 0;
                    s.Probed.Add(beat);
                    s.Act = GuidedAct.Act;
                    s.ActS = clockS;
                }
                break;
            case GuidedAct.Comms:
                if (CommsCheck(console, context, s, clockS))
                {
                    s.CommsDone = true;
                    s.Act = GuidedAct.Act;
                    s.ActS = clockS;
                }
                break;
            case GuidedAct.Act:
                // The player's reaction: the settle time above plus this, from the callout appearing to the click.
                if (clockS - s.ActS < Math.Max(0.1, _guidedReactionS - BeatSettleS) || hit is null) break;
                s.Tries++;
                if (order.IsVisibleInTree())
                {
                    GD.Print($"[Play] guide '{beat}': pressing '{order.Text}' (try {s.Tries})");
                    ClickControl(order);
                }
                else if ((TutorialAnchorIds.CompletesOnPress.Contains(AnchorOf(beat)) && !(AnchorOf(beat) == TutorialAnchorIds.AlarmAck && hit.Value is { IsTarget: false }))
                         || (hit.Value is { IsTarget: false } && AnchorOf(beat) != TutorialAnchorIds.AlarmAck))
                {
                    var r = hit.Value.Rect;
                    GD.Print($"[Play] guide '{beat}': clicking the ring ({(hit.Value.IsTarget ? "target" : "way to it")}) at {r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0} (try {s.Tries})");
                    if (hit.Value.IsTarget && AnchorOf(beat) is TutorialAnchorIds.PlannerEngine or TutorialAnchorIds.PlannerGoal
                        && console.MainView.Orbit.FindChildren(AnchorOf(beat) == TutorialAnchorIds.PlannerEngine ? "EngineSelect" : "GoalSelect", "", true, false)
                            .OfType<OptionButton>().FirstOrDefault(o => o.IsVisibleInTree()) is { } option)
                    {
                        s.Dropdown = (option, AnchorOf(beat) == TutorialAnchorIds.PlannerEngine ? "etro" : zoneWord, clockS, beat);
                    }
                    Click(new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2));
                }
                else if (gotIt.IsVisibleInTree())
                {
                    GD.Print($"[Play] guide '{beat}': pressing '{gotIt.Text}'");
                    ClickControl(gotIt);
                }
                else
                {
                    // On the rails a wait step (and the wait between steps) has no button: the game moves on by itself.
                    GD.Print($"[Play] guide '{beat}': no button, waiting");
                    s.Act = GuidedAct.Passive;
                    break;
                }
                s.Act = GuidedAct.Wait;
                s.ActS = clockS;
                break;
            case GuidedAct.Passive:
                if (order.IsVisibleInTree() || gotIt.IsVisibleInTree())
                {
                    s.Act = GuidedAct.Act;
                    s.ActS = clockS;
                }
                break;
            case GuidedAct.Wait:
                // A sent order that needs confirming: the normal dialog (the guide's own confirm beat clicks its ring instead).
                if (beat != "walk_retro_confirm" && VisibleConfirm(console) is { } confirm)
                {
                    if (s.ConfirmSeenS < 0) s.ConfirmSeenS = clockS;
                    if (clockS - s.ConfirmSeenS < Math.Min(1.0, _guidedReactionS)) break;
                    s.ConfirmSeenS = -1;
                    if (!s.ConfirmShot)
                    {
                        s.ConfirmShot = true;
                        Capture($"{s.Seen.Count:00}b_{beat}_confirm");
                    }
                    GD.Print($"[Play] guide '{beat}': confirming '{confirm.Text}'");
                    ClickControl(confirm);
                    s.ActS = clockS;
                    break;
                }
                // Still up: press again (crew busy, a click that missed, a ring that moved to the next control).
                if (clockS - s.ActS > (hit is { IsTarget: false } ? 2 : 20))
                {
                    if (s.Tries >= 6)
                    {
                        Fail($"guide '{beat}': still up after {s.Tries} presses ({Describe()})");
                        Capture($"{s.Seen.Count:00}x_{beat}_stuck");
                        ClickControl(gotIt);
                        s.Act = GuidedAct.Done;
                    }
                    else
                    {
                        s.Act = GuidedAct.Act;
                    }
                }
                break;
        }

        if (since > BeatStuckS && s.Act != GuidedAct.Done)
        {
            Fail($"guide '{beat}': up for {since:0} s real ({Describe()})");
            Capture($"{s.Seen.Count:00}x_{beat}_stuck");
            ClickControl(gotIt);
            s.Act = GuidedAct.Done;
        }
    }

    private static string StepOf(string beat) =>
        beat.IndexOf(TutorialCallout.RailsMark, StringComparison.Ordinal) is var mark and >= 0 ? beat[..mark] : beat;

    private static string RailsState(Usa.Game.Integration.Runtime.MissionRuntimeContext context) =>
        context.Rails?.Step is { } step ? $"step {step.Number} {step.Phase}" : "off";

    /// <summary>
    /// One off-script check on the rails: types an order the step does not allow, waits for the crew's answer, clicks a
    /// greyed-out control, then checks that no command went through and the step did not move.
    /// </summary>
    private bool RailsProbe(MissionConsole console, Usa.Game.Integration.Runtime.MissionRuntimeContext context, GuidedState s, double clockS, string beat)
    {
        var messages = context.Transcript.Messages;
        var stepS = clockS - s.ProbeS;
        switch (s.ProbeStep)
        {
            case 0:
            {
                if (console.FindChildren("*", "", true, false).OfType<CommsPanel>().FirstOrDefault()?.GetNodeOrNull<LineEdit>("%Input") is not { } input)
                {
                    Fail($"probe '{beat}': no comms input");
                    return true;
                }
                s.ProbeMark = messages.Count;
                s.ProbeSubmitMark = s.Submits.Count;
                s.ProbeStepBefore = RailsState(context);
                TypeInto(input, ProbeOrders[beat]);
                return NextProbe();
            }
            case 1:
            {
                var redirected = messages.Skip(s.ProbeMark).Any(m => m.Text.Contains("stick to the checklist", StringComparison.OrdinalIgnoreCase));
                if (stepS < 2 || (!redirected && stepS < 10)) return false;
                foreach (var m in messages.Skip(s.ProbeMark))
                    GD.Print($"[Play] probe   {m.Met} {m.Kind} {m.SpeakerName ?? m.Speaker?.Value ?? "-"}: {m.Text}" + (m.Options.Count > 0 ? $"  [chips: {string.Join(" | ", m.Options)}]" : ""));
                if (!redirected) Fail($"probe '{beat}': off-script '{ProbeOrders[beat]}' got no redirect line");
                Capture($"{s.Seen.Count:00}r_{beat}_offscript");
                GetViewport().GuiReleaseFocus();
                var greyed = console.FindChildren("*", "", true, false).OfType<BaseButton>()
                    .Where(b => b.Disabled && b.IsVisibleInTree() && b.TooltipText.StartsWith("Follow the guide", StringComparison.Ordinal)).ToArray();
                if (greyed.Length == 0)
                {
                    Fail($"probe '{beat}': no greyed-out control on screen");
                    return NextProbe();
                }
                var pick = greyed[s.Probed.Count * (greyed.Length - 1) / Math.Max(1, ProbeOrders.Count - 1)];
                GD.Print($"[Play] probe '{beat}': clicking greyed-out {pick.Name} '{(pick as Button)?.Text}' (tooltip '{pick.TooltipText}'; {greyed.Length} greyed on screen)");
                ClickControl(pick);
                return NextProbe();
            }
            case 2:
            {
                if (stepS < 1.5) return false;
                var after = RailsState(context);
                var submits = s.Submits.Skip(s.ProbeSubmitMark).ToArray();
                var executed = submits.Where(x => x.Contains("-> Valid", StringComparison.Ordinal) || x.Contains("-> NeedsConfirmation", StringComparison.Ordinal)).ToArray();
                GD.Print($"[Play] probe '{beat}': rails {s.ProbeStepBefore} -> {after}; submissions during the probe: {(submits.Length == 0 ? "none" : string.Join(" ; ", submits))}");
                if (after != s.ProbeStepBefore) Fail($"probe '{beat}': the off-script order or greyed click moved the rails");
                if (executed.Length > 0) Fail($"probe '{beat}': an off-script input went through: {string.Join(" ; ", executed)}");
                else GD.Print($"[Play] probe '{beat}': nothing executed");
                return true;
            }
            default:
                return true;
        }

        bool NextProbe()
        {
            s.ProbeStep++;
            s.ProbeS = clockS;
            return false;
        }
    }

    /// <summary>Captures the docking approach and the parachute descent, and presses N in the calm after docking.</summary>
    private void WatchMoments(MissionConsole console, GuidedState s, double clockS, string? beat)
    {
        var host = _root.Services.Flow.Host;
        // The approach and the chutes play in the External view; the guide leaves the player on Orbit, so switch (key 2).
        if (_guidedExternal && beat is { } w && ((StepOf(w) == "walk_dock" && w != "walk_dock" && s.DockShots == 0) || (s.CurrentStep == "walk_entry" && s.ChuteShots == 0))
            && (MainViewKind)console.MainView.SelectedView != MainViewKind.External && clockS - s.ExternalKeyS > 1)
        {
            s.ExternalKeyS = clockS;
            GetViewport().GuiReleaseFocus();
            PressKey(Key.Key2);
            GD.Print("[Play] pressed 2 (External view)");
            return;
        }

        if (beat is { } b && StepOf(b) == "walk_dock" && b != "walk_dock" && s.DockShots < 6 && clockS - s.DockShotS >= 0.6)
        {
            s.DockShots++;
            s.DockShotS = clockS;
            GD.Print($"[Play] docking approach frame {s.DockShots} at MET {host.LastFrame.MetAfter}, view {(MainViewKind)console.MainView.SelectedView}");
            Capture($"dock_{s.DockShots}");
        }

        if (s.CurrentStep == "walk_entry" && s.ChuteShots < 30 && clockS - s.ChuteShotS >= 2.0)
        {
            s.ChuteShots++;
            s.ChuteShotS = clockS;
            GD.Print($"[Play] descent frame {s.ChuteShots} at MET {host.LastFrame.MetAfter}, view {(MainViewKind)console.MainView.SelectedView}, rate x{host.EffectiveRate:0.#}");
            Capture($"chute_{s.ChuteShots:00}");
        }

        var rateLabel = console.FindChildren("*", "", true, false).OfType<ConsoleHeader>().FirstOrDefault()?.GetNodeOrNull<Label>("%CompressionRate");
        switch (s.AdvanceStep)
        {
            case 0 when s.CurrentStep == "walk_docked" && clockS - s.CurrentStepS >= 1.0:
                GetViewport().GuiReleaseFocus();
                GD.Print($"[Play] pressing N in the calm after docking (header '{rateLabel?.Text}', can advance {host.CanAdvanceToNextEvent})");
                PressKey(Key.N);
                s.AdvanceStep = 1;
                s.AdvanceS = clockS;
                break;
            case 1:
                if (host.EffectiveRate > s.AdvanceMaxRate)
                {
                    s.AdvanceMaxRate = host.EffectiveRate;
                    s.AdvanceMaxText = rateLabel?.Text ?? "";
                }
                if (!s.AdvanceShot && clockS - s.AdvanceS > 1.5)
                {
                    s.AdvanceShot = true;
                    Capture("02n_after_N");
                }
                if (clockS - s.AdvanceS < 5 && s.CurrentStep == "walk_docked") break;
                GD.Print($"[Play] after N: peak rate x{s.AdvanceMaxRate:0.#}, header then '{s.AdvanceMaxText}' (advancing {host.IsAdvancingToEvent})");
                s.AdvanceStep = 2;
                break;
        }
    }

    /// <summary>No callout: once the run is over, the player still has to find Proceed (the guide should have pointed at it).</summary>
    private void ProceedWithoutGuide(MissionConsole console, double clockS, GuidedState s)
    {
        var main = console.MainView;
        var proceed = main.RecoveryCard.GetNodeOrNull<Button>("%ProceedButton") is { Disabled: false } r && r.IsVisibleInTree() ? r
            : main.ClosingCard.GetNodeOrNull<Button>("%Proceed") is { Disabled: false } c && c.IsVisibleInTree() ? c : null;
        if (proceed is null)
        {
            s.ProceedSeenS = -1;
            return;
        }
        if (s.ProceedSeenS < 0)
        {
            s.ProceedSeenS = clockS;
            return;
        }
        if (clockS - s.ProceedSeenS < 30) return;
        Fail("guided: Proceed on screen for 30 s with no guide callout pointing at it");
        Capture("98_proceed_without_guide");
        ClickControl(proceed);
        s.ProceedSeenS = clockS;
    }

    /// <summary>
    /// Comms fixes, as typed by a player while the first callout waits: a question about an unknown value (expect June's
    /// offer to check), "yes" (expect the read), then a resource tile's Ask sent unchanged (expect no "Say again").
    /// </summary>
    private bool CommsCheck(MissionConsole console, Usa.Game.Integration.Runtime.MissionRuntimeContext context, GuidedState s, double clockS)
    {
        var messages = context.Transcript.Messages;
        var panel = console.FindChildren("*", "", true, false).OfType<CommsPanel>().FirstOrDefault();
        if (panel?.GetNodeOrNull<LineEdit>("%Input") is not { } input) return Give("no comms input");
        var stepS = clockS - s.CommsS;
        switch (s.CommsStep)
        {
            case 0:
                s.CommsMark = messages.Count;
                TypeInto(input, "what's fuel cell 2 putting out?");
                return Next();
            case 1:
            {
                var offered = messages.Skip(s.CommsMark).FirstOrDefault(m => m.Offer is not null);
                if (offered is null) return stepS > 40 ? Give("no crew offer after the fuel cell question") : false;
                if (stepS < 2.5) return false;
                Capture($"{s.Seen.Count:00}c_comms_offer");
                Dump(s.CommsMark);
                s.CommsMark = messages.Count;
                TypeInto(input, "yes");
                return Next();
            }
            case 2:
            {
                var fresh = messages.Skip(s.CommsMark).ToArray();
                var read = fresh.FirstOrDefault(m => m.Facts.Count > 0 && m.Kind == TranscriptMessageKind.Crew);
                if (fresh.Any(m => m.Text.Contains("Say again", StringComparison.OrdinalIgnoreCase))) return Give("\"yes\" to the offer got \"Say again\"");
                if (read is null) return stepS > 90 ? Give("no reading after \"yes\"") : false;
                if (stepS < 1.5) return false;
                Capture($"{s.Seen.Count:00}d_comms_yes_read");
                Dump(s.CommsMark);
                return Next();
            }
            case 3:
            {
                var tile = console.FindChildren("*", "", true, false).OfType<ResourceStrip>().FirstOrDefault()?
                    .FindChildren("*", "", true, false).OfType<ReadoutTile>()
                    .Select(t => t.GetNodeOrNull<Button>("%Ask")).FirstOrDefault(b => b is { Disabled: false } && b.IsVisibleInTree());
                if (tile is null)
                {
                    GD.Print("[Play] comms: no resource tile offers Ask now; Ask check skipped");
                    return true;
                }
                s.CommsMark = messages.Count;
                GD.Print($"[Play] comms: pressing a resource tile's Ask ({tile.GetParent()?.GetParent()?.Name})");
                ClickControl(tile);
                return Next();
            }
            case 4:
                if (stepS < 1) return false;
                GD.Print($"[Play] comms: Ask pre-filled '{input.Text}' (focus {input.HasFocus()})");
                Capture($"{s.Seen.Count:00}e_ask_prefill");
                if (!input.HasFocus()) ClickControl(input);
                PressKey(Key.Enter);
                return Next();
            case 5:
            {
                if (stepS < 12) return false;
                var fresh = messages.Skip(s.CommsMark).ToArray();
                Capture($"{s.Seen.Count:00}f_ask_sent");
                Dump(s.CommsMark);
                if (fresh.Any(m => m.Text.Contains("Say again", StringComparison.OrdinalIgnoreCase))) Fail("comms: a resource tile's Ask got \"Say again\"");
                else if (fresh.Length == 0) Fail("comms: a resource tile's Ask sent nothing");
                else GD.Print("[Play] comms: Ask went out without \"Say again\"");

                return true;
            }
            default:
                return true;
        }

        bool Next()
        {
            s.CommsStep++;
            s.CommsS = clockS;
            return false;
        }

        bool Give(string why)
        {
            Fail($"comms: {why}");
            Capture($"{s.Seen.Count:00}x_comms_failed");
            Dump(s.CommsMark);
            return true;
        }

        void Dump(int from)
        {
            foreach (var m in messages.Skip(from))
                GD.Print($"[Play] comms   {m.Met} {m.Kind} {m.SpeakerName ?? m.Speaker?.Value ?? "-"}: {m.Text}" +
                         (m.Offer is { } o ? $"  [offer: {o.Label}]" : "") + (m.Options.Count > 0 ? $"  [chips: {string.Join(" | ", m.Options)}]" : ""));
        }
    }

    private void LogBeat(TutorialCalloutView view, TutorialAnchorHit hit, string beat, Dictionary<string, double?> caps, double rate, int index)
    {
        var callout = view.GetGlobalRect();
        var ring = new Rect2(hit.Rect.X, hit.Rect.Y, hit.Rect.Width, hit.Rect.Height);
        var screen = GetViewport().GetVisibleRect();
        var covers = callout.Intersects(ring) ? $"COVERS RING by {callout.Intersection(ring).Area:0} px²" : "clear of ring";
        var inside = screen.Encloses(callout) ? "on screen" : "OFF SCREEN";
        var cap = caps.GetValueOrDefault(beat);
        var rateNote = cap is { } c && rate > c * 1.05 ? $"RATE x{rate:0.#} ABOVE CAP x{c:0.#}" : $"rate x{rate:0.#}{(cap is { } c2 ? $" (cap x{c2:0.#})" : "")}";
        GD.Print(
            $"[Play] beat {index:00} {beat}: anchor {AnchorOf(beat)} ring {ring.Position.X:0},{ring.Position.Y:0} {ring.Size.X:0}x{ring.Size.Y:0} " +
            $"({(hit.IsTarget ? "target" : "way to it")}), callout {callout.Position.X:0},{callout.Position.Y:0} {callout.Size.X:0}x{callout.Size.Y:0}, " +
            $"{covers}, {inside}, {rateNote}, button {(view.GetNode<Button>("%GotIt") is { Visible: true } g ? $"'{g.Text}'" : "none")}" +
            (view.GetNode<Button>("%Order") is { Visible: true } o ? $", send '{o.Text}'" : ""));
        if (!screen.Encloses(callout)) Fail($"guide '{beat}': callout off screen");
        if (callout.Intersects(ring) && callout.Intersection(ring).Area > 4) Fail($"guide '{beat}': callout covers its ring");
    }

    private static Dictionary<string, double?> WalkCaps()
    {
        var caps = new Dictionary<string, double?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(TutorialPath)));
        foreach (var beat in doc.RootElement.GetProperty("beats").EnumerateArray())
        {
            var id = beat.GetProperty("id").GetString() ?? "";
            if (!id.StartsWith("walk_", StringComparison.Ordinal)) continue;
            caps[id] = beat.TryGetProperty("maxTimeRate", out var cap) ? cap.GetDouble() : null;
        }
        return caps;
    }

    private string AnchorOf(string beat)
    {
        _anchorsByBeat ??= LoadAnchors();
        return _anchorsByBeat.GetValueOrDefault(beat, "");
    }

    private Dictionary<string, string>? _anchorsByBeat;

    private static Dictionary<string, string> LoadAnchors()
    {
        var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(TutorialPath)));
        foreach (var beat in doc.RootElement.GetProperty("beats").EnumerateArray())
            anchors[beat.GetProperty("id").GetString() ?? ""] = beat.GetProperty("anchor").GetString() ?? "";
        return anchors;
    }

    private static Button? VisibleConfirm(MissionConsole console) =>
        console.FindChildren("*", "", true, false).OfType<ConfirmDialog>().FirstOrDefault()?.GetNodeOrNull<Button>("%Confirm") is { Disabled: false } confirm
        && confirm.IsVisibleInTree()
            ? confirm
            : null;

    private void PickFromPopup(OptionButton option, string wanted, string shot)
    {
        var popup = option.GetPopup();
        var index = Enumerable.Range(0, option.ItemCount).FirstOrDefault(i => option.GetItemText(i).Contains(wanted, StringComparison.OrdinalIgnoreCase), -1);
        var items = string.Join(" | ", Enumerable.Range(0, option.ItemCount).Select(option.GetItemText));
        if (!popup.Visible) Fail($"dropdown {option.Name}: the click did not open its list");
        else Capture(shot);
        if (index < 0)
        {
            Fail($"dropdown {option.Name}: no item with '{wanted}' in [{items}]");
            return;
        }
        // The item click, as the popup reports it to its OptionButton.
        popup.EmitSignal(PopupMenu.SignalName.IndexPressed, index);
        popup.Hide();
        GD.Print($"[Play] dropdown {option.Name}: picked '{option.GetItemText(option.Selected)}' from [{items}]");
        if (option.Selected != index) Fail($"dropdown {option.Name}: picking item {index} left {option.Selected} selected");
    }

    private void ClickControl(Control control)
    {
        var xform = control.GetGlobalTransformWithCanvas();
        Click(xform * (control.Size / 2));
    }

    /// <summary>A left click at a canvas position (viewport pixels), as the window would deliver it.</summary>
    private void Click(Vector2 canvas)
    {
        var at = GetViewport().GetFinalTransform() * canvas;
        Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Godot.Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true,
        });
        Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
    }

    /// <summary>Clicks the field, then types the text key by key and presses Enter.</summary>
    private void TypeInto(LineEdit input, string text)
    {
        ClickControl(input);
        foreach (var ch in text)
        {
            var key = KeyFor(ch);
            Godot.Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key, Unicode = ch, ShiftPressed = ch is '?' or '\"' || char.IsUpper(ch) });
            Godot.Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key, Unicode = ch });
        }
        PressKey(Key.Enter);
        GD.Print($"[Play] comms: typed '{text}' + Enter");
    }

    private static void PressKey(Key key)
    {
        Godot.Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key });
        Godot.Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key });
    }

    private static Key KeyFor(char ch) => char.ToUpperInvariant(ch) switch
    {
        >= 'A' and <= 'Z' and var c => Key.A + (c - 'A'),
        >= '0' and <= '9' and var d => Key.Key0 + (d - '0'),
        ' ' => Key.Space,
        '?' => Key.Slash,
        '\'' => Key.Apostrophe,
        ',' => Key.Comma,
        '.' => Key.Period,
        _ => Key.Unknown,
    };
}
