using System.Globalization;
using System.Text;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.Diagnostics.Overlay;

/// <summary>Plain-text body of the F12 debug overlay.</summary>
public static class DebugOverlayText
{
    public const int CommandRows = 10;
    public const int HashDigits = 16;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Status(DebugOverlayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var text = new StringBuilder();
        text.Append(Invariant, $"build {frame.Version} | screen {frame.Screen}\n");
        text.Append(Invariant, $"fps {frame.Fps:0} | frame {frame.FrameMs:0.0} ms | process {frame.ProcessMs:0.0} ms\n");
        text.Append(Invariant, $"backend {frame.Backend}\n");
        text.Append(Invariant, $"app freeze {frame.AppFreeze}\n");

        if (frame.Mission is not { } mission)
        {
            text.Append("no mission running\n");
        }
        else
        {
            text.Append(Invariant, $"mission {mission.MissionId} | {mission.Difficulty} | seed {mission.Seed} | run tag {mission.RunTag ?? "none"}\n");
            text.Append(Invariant, $"MET {mission.Met} | x{mission.CompressionRate:0.##} ({mission.CompressionReason}), effective x{mission.EffectiveRate:0.##}");
            if (mission.AdvancingToEvent) text.Append(" | advancing to next event");
            text.Append('\n');
            text.Append(Invariant, $"session freeze {mission.SessionFreeze}\n");
            text.Append(mission.LastHash is { } last
                ? string.Create(Invariant, $"hash @ {last.Time} {HashPrefix(last.Hash)} ({mission.HashSamples} samples)\n")
                : "hash: no 60 s sample yet\n");
            if (mission.LiveHash is { } live) text.Append(Invariant, $"hash now @ {live.Time} {HashPrefix(live.Hash)}\n");
        }

        if (frame.Notice is { Length: > 0 } notice) text.Append(Invariant, $"» {notice}\n");
        return text.ToString().TrimEnd('\n');
    }

    /// <summary>The newest <see cref="CommandRows"/> logged commands, oldest first.</summary>
    public static string Commands(DebugOverlayMission? mission)
    {
        if (mission is null) return "";
        var entries = mission.Commands;
        if (entries.Count == 0) return "no commands yet";
        var text = new StringBuilder();
        text.Append(Invariant, $"last {Math.Min(CommandRows, entries.Count)} of {entries.Count} commands\n");
        for (var i = Math.Max(0, entries.Count - CommandRows); i < entries.Count; i++) text.Append(Command(entries[i])).Append('\n');
        return text.ToString().TrimEnd('\n');
    }

    public static string Command(CommandLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var request = entry.Request;
        var text = new StringBuilder();
        text.Append(Invariant, $"{new MissionTime(entry.MetMs)} {request.Id.Value} {request.Action.Value}");
        if (request.Assignee is { } assignee) text.Append(Invariant, $" @{assignee.Value}");
        if (request.Args.Count > 0)
        {
            text.Append(' ');
            var first = true;
            foreach (var name in request.Args.Keys.Order(StringComparer.Ordinal))
            {
                if (!first) text.Append(',');
                first = false;
                text.Append(Invariant, $"{name}={request.Args[name].ToCanonicalString()}");
            }
        }
        text.Append(Invariant, $" [{request.Source}{(request.Confirmed ? ", confirmed" : "")}]");
        return text.ToString();
    }

    public static string HashPrefix(string hash) => hash.Length <= HashDigits ? hash : hash[..HashDigits];
}
