using System.Globalization;
using System.Text;
using Usa.Game.Comms;
using Usa.Game.Integration.Runtime;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.Presentation.Map.Planning;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Diagnostics.Harness;

/// <summary>Plain-text views of a composed runtime for the debug harness: only what the console and comms would show.</summary>
public static class HarnessRuntimeText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The console snapshot (knowledge-filtered values with source, MET and badges).</summary>
    public static string Console(ConsoleSnapshot? snapshot)
    {
        if (snapshot is null) return "No console snapshot yet.";
        var text = new StringBuilder();
        text.Append(Invariant, $"{snapshot.MissionTitle} · {snapshot.VehicleName} · {ReadoutFormatter.FormatMet(snapshot.Met)} · {snapshot.Phase} · link {snapshot.Link}");
        text.Append(Invariant, $" · x{snapshot.Compression.Rate:0.##} ({snapshot.Compression.Reason})\n");
        if (snapshot.Objectives.Count > 0)
            text.Append("Objectives: ").AppendJoin("; ", snapshot.Objectives.Select(o => $"{o.Label} [{o.State}]")).Append('\n');

        text.Append("Resources\n");
        foreach (var tile in snapshot.Resources)
        {
            text.Append(Invariant, $"  {tile.Label,-12} {Readout(tile.Primary, snapshot.Met)}");
            if (tile.Secondary is { } secondary) text.Append(" | ").Append(Readout(secondary, snapshot.Met));
            if (tile.Detail is { } detail) text.Append(Invariant, $" [{detail}]");
            text.Append('\n');
        }

        if (snapshot.Forecast is { } forecast)
            text.Append(Invariant, $"Forecast: {forecast.Resources.Count} resource line(s), coverage {forecast.Coverage}\n");
        if (snapshot.Crew.Count > 0)
            text.Append("Crew: ").AppendJoin("; ", snapshot.Crew.Select(c => $"{c.Name} {(c.HealthPercent is { } h ? h.ToString("0", Invariant) + "%" : "?")}")).Append('\n');
        if (snapshot.ActiveTask is { } task)
            text.Append(Invariant, $"Task: {task.Name} ({task.AssigneeName}) until {ReadoutFormatter.FormatMet(task.EstimatedEnd)}\n");
        foreach (var alarm in snapshot.Alarms)
            text.Append(Invariant, $"ALARM {alarm.Priority} {alarm.Text}{(alarm.Acknowledged ? " (ack)" : "")}\n");

        foreach (var tab in snapshot.SystemsBoard)
        {
            foreach (var group in tab.ReadoutGroups)
            {
                if (group.Readouts.Count == 0) continue;
                text.Append(Invariant, $"{tab.Tab} / {group.Title}\n");
                foreach (var readout in group.Readouts) text.Append(Invariant, $"  {readout.Label}: {Readout(readout.Value, snapshot.Met)}\n");
            }
        }
        return text.ToString();
    }

    public static string Readout(ReadoutSnapshot readout, MissionTime now)
    {
        var formatted = ReadoutFormatter.Format(readout, now);
        var text = new StringBuilder(formatted.Value);
        if (formatted.Badge is { } badge) text.Append(Invariant, $" {badge}");
        if (formatted.Meta is { } meta) text.Append(Invariant, $" ({meta})");
        if (formatted.SecondaryValue is { } second) text.Append(Invariant, $" ⇄ {second}");
        return text.ToString();
    }

    public static string Transcript(Transcript transcript, int maxLines)
    {
        var messages = transcript.Messages;
        var text = new StringBuilder();
        for (var i = Math.Max(0, messages.Count - maxLines); i < messages.Count; i++)
        {
            var message = messages[i];
            var who = message.SpeakerName ?? message.Kind.ToString();
            text.Append(Invariant, $"{ReadoutFormatter.FormatMetShort(message.Met)} {who}: {message.Text}\n");
        }
        return text.Length == 0 ? "(no messages)" : text.ToString();
    }

    public static string Tip(GuidanceTipSnapshot? tip, MissionTime? cooldownUntil, MissionTime now)
    {
        var cooldown = cooldownUntil is { } until && until > now ? $"next tip {ReadoutFormatter.FormatRelative(now, until)}" : "no cooldown";
        return tip is null ? $"No tip ({cooldown})." : $"[{tip.Stage}] {tip.Text}\n({tip.TipId}, MET {ReadoutFormatter.FormatMetShort(tip.IssuedAt)}; {cooldown})";
    }

    public static string EntryHud(EntryHudModel hud, SplashdownCardModel splashdown)
    {
        var text = new StringBuilder();
        text.Append(Invariant, $"Alt {hud.AltitudeText} · V {hud.VelocityText} · FPA {hud.FlightPathAngleText}\n");
        text.Append(Invariant, $"g {hud.GLoad.Text} · heat {hud.Heating.Text} · {hud.Drogue.Label} {hud.Drogue.Text} · {hud.Main.Label} {hud.Main.Text}\n");
        text.Append(hud.SequenceText).Append('\n');
        if (hud.Checklist is { } checklist) text.Append(Invariant, $"{ChecklistProgress.Title}: {checklist.Text}\n");
        if (splashdown.Visible)
            text.Append(Invariant, $"Splashdown: {splashdown.ZoneText}, {splashdown.DistanceText}, wait {splashdown.WaitRemainingText}; {splashdown.CrewSummary}\n");
        return text.ToString();
    }

    public static string Planner(BurnPlannerModel planner)
    {
        var text = new StringBuilder();
        foreach (var engine in planner.Engines)
        {
            text.Append(Invariant, $"{engine.Label}: {(engine.Available ? "available" : $"unavailable — {engine.UnavailableReason}")}");
            if (engine.KnownPropellant is { } known)
                text.Append(Invariant, $" · propellant {known.State} {known.LowKg:0}-{known.HighKg:0} kg{(known.Source is { Length: > 0 } source ? $" ({source})" : "")}");
            text.Append('\n');
        }
        return text.Length == 0 ? "(no engines)" : text.ToString();
    }

    public static string Confirmation(ConfirmationRequest? request) =>
        request is null ? "No confirmation pending." : $"Confirm? {request.Title} ({string.Join(", ", request.Arguments.Select(a => $"{a.Key}={a.Value}"))})";
}
